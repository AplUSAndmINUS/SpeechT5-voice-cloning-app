using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VoiceCloningApp.Services;

/// <summary>
/// Detects whether the three SpeechT5 models are present in the backend
/// <c>models/</c> directory and orchestrates the download script when needed.
/// </summary>
public class ModelSetupService
{
  private static readonly string[] _modelSubDirs =
      ["speecht5_tts", "speecht5_hifigan", "spkrec-xvect-voxceleb"];

  /// <summary>
  /// Absolute path to the folder that contains <c>app.py</c>, discovered by
  /// walking up the directory tree from <see cref="AppContext.BaseDirectory"/>.
  /// <c>null</c> when the backend root cannot be located.
  /// </summary>
  public string? BackendRoot { get; }

  public ModelSetupService()
  {
    BackendRoot = FindBackendRoot();
  }

  /// <summary>Returns <c>true</c> when <c>app.py</c> was found on disk.</summary>
  public bool IsBackendRootFound => BackendRoot is not null;

  /// <summary>Returns <c>true</c> when every model sub-directory is non-empty.</summary>
  public bool AreAllModelsInstalled()
  {
    if (BackendRoot is null) return false;
    return _modelSubDirs.All(IsModelInstalled);
  }

  /// <summary>Returns install status for each model sub-directory.</summary>
  public IReadOnlyList<(string Name, bool Installed)> GetModelStatuses()
      => _modelSubDirs.Select(d => (d, IsModelInstalled(d))).ToList();

  /// <summary>
  /// Runs the platform-appropriate download script and yields each log line
  /// as it is written to stdout/stderr. Finishes with a success or failure
  /// summary line.
  /// </summary>
  public async IAsyncEnumerable<string> DownloadModelsAsync(
      [EnumeratorCancellation] CancellationToken ct = default)
  {
    if (BackendRoot is null)
    {
      yield return "ERROR: Could not locate app.py — backend root is unknown.";
      yield break;
    }

    ProcessStartInfo psi;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      var scriptPath = Path.Combine(BackendRoot, "scripts", "download_models.ps1");
      if (!File.Exists(scriptPath))
      {
        yield return $"ERROR: Script not found: {scriptPath}";
        yield break;
      }

      psi = new ProcessStartInfo
      {
        FileName = "powershell.exe",
        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File \"{scriptPath}\"",
        WorkingDirectory = BackendRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
    }
    else
    {
      var scriptPath = Path.Combine(BackendRoot, "scripts", "download_models.sh");
      if (!File.Exists(scriptPath))
      {
        yield return $"ERROR: Script not found: {scriptPath}";
        yield break;
      }

      psi = new ProcessStartInfo
      {
        FileName = "/bin/bash",
        Arguments = $"\"{scriptPath}\"",
        WorkingDirectory = BackendRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
    }

    using var process = new Process { StartInfo = psi };

    process.Start();

    try
    {
      // Stream stdout line by line; ReadLineAsync returns null at end-of-stream
      while (!ct.IsCancellationRequested)
      {
        var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null) break;
        yield return line;
      }

      if (ct.IsCancellationRequested)
      {
        yield return "⚠️ Download cancelled.";
        yield break;
      }

      // Capture any stderr
      var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
      if (!string.IsNullOrWhiteSpace(stderr))
      {
        foreach (var errLine in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
          yield return $"[stderr] {errLine.TrimEnd()}";
      }

      await process.WaitForExitAsync(ct).ConfigureAwait(false);

      yield return process.ExitCode == 0
          ? "✅ All models downloaded successfully."
          : $"❌ Download script exited with code {process.ExitCode}.";
    }
    finally
    {
      if (!process.HasExited)
      {
        try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
      }
    }
  }

  // -------------------------------------------------------------------------

  private bool IsModelInstalled(string modelSubDir)
  {
    if (BackendRoot is null) return false;
    var path = Path.Combine(BackendRoot, "models", modelSubDir);
    return Directory.Exists(path) && Directory.EnumerateFiles(path).Any();
  }

  private static string? FindBackendRoot()
  {
    // Walk up from the executable directory looking for app.py
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
      if (File.Exists(Path.Combine(dir.FullName, "app.py")))
        return dir.FullName;
      dir = dir.Parent;
    }
    return null;
  }
}
