using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace VoiceCloningApp.Services;

/// <summary>Current server process state.</summary>
public enum BackendStatus { Stopped, Starting, Running, Error }

/// <summary>
/// Manages the lifecycle of the local uvicorn / Python backend process.
/// Register as a singleton so the process survives page navigation.
/// </summary>
public class BackendProcessService : IDisposable
{
  private readonly ILogger<BackendProcessService> _logger;
  private readonly object _lock = new();
  private Process? _process;
  private CancellationTokenSource? _startCts;

  private readonly List<string> _logLines = [];

  /// <summary>Current server process state.</summary>
  public BackendStatus Status { get; private set; } = BackendStatus.Stopped;

  /// <summary>Last error message when <see cref="Status"/> is <see cref="BackendStatus.Error"/>.</summary>
  public string? LastError { get; private set; }

  /// <summary>Lines captured from the server's stdout/stderr since last start.</summary>
  public IReadOnlyList<string> LogLines => _logLines;

  /// <summary>Raised on the calling thread whenever status or log lines change.</summary>
  public event Action? StatusChanged;

  public BackendProcessService(ILogger<BackendProcessService> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Starts uvicorn in <paramref name="backendRoot"/>, then polls <c>/health</c>
  /// until the service is ready (up to 90 seconds).
  /// </summary>
  public async Task StartAsync(string backendRoot, CancellationToken ct = default)
  {
    lock (_lock)
    {
      if (Status is BackendStatus.Starting or BackendStatus.Running)
        return;

      _logLines.Clear();
      LastError = null;
      SetStatus(BackendStatus.Starting);
    }

    _startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    try
    {
      var psi = BuildProcessStartInfo(backendRoot);
      _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

      _process.OutputDataReceived += (_, e) =>
      {
        if (e.Data is null) return;
        lock (_lock) _logLines.Add(e.Data);
        StatusChanged?.Invoke();
      };

      _process.ErrorDataReceived += (_, e) =>
      {
        if (e.Data is null) return;
        lock (_lock) _logLines.Add($"[err] {e.Data}");
        StatusChanged?.Invoke();
      };

      _process.Exited += OnProcessExited;

      if (!_process.Start())
      {
        throw new InvalidOperationException("Process.Start() returned false.");
      }

      _process.BeginOutputReadLine();
      _process.BeginErrorReadLine();

      AddLog("Backend process started — waiting for /health…");

      // Poll until healthy or timeout
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_startCts.Token);
      timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

      using var http = new HttpClient { BaseAddress = new Uri("http://localhost:8000") };

      while (!timeoutCts.Token.IsCancellationRequested)
      {
        try
        {
          var response = await http.GetAsync("/health", timeoutCts.Token)
              .ConfigureAwait(false);
          if (response.IsSuccessStatusCode)
          {
            AddLog("✅ Backend is ready.");
            SetStatus(BackendStatus.Running);
            return;
          }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* not ready yet */ }

        await Task.Delay(1500, timeoutCts.Token).ConfigureAwait(false);
      }

      SetError("Backend did not become healthy within 90 seconds.");
    }
    catch (OperationCanceledException)
    {
      SetError("Startup was cancelled.");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start backend process.");
      SetError(ex.Message);
    }
    finally
    {
      _startCts?.Dispose();
      _startCts = null;
    }
  }

  /// <summary>Stops the running backend process.</summary>
  public void Stop()
  {
    _startCts?.Cancel();

    lock (_lock)
    {
      try
      {
        if (_process is { HasExited: false })
        {
          _process.Kill(entireProcessTree: true);
          AddLog("Backend process stopped.");
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error while stopping backend process.");
      }
      finally
      {
        _process?.Dispose();
        _process = null;
        SetStatus(BackendStatus.Stopped);
      }
    }
  }

  public void Dispose() => Stop();

  // -------------------------------------------------------------------------
  // Private helpers
  // -------------------------------------------------------------------------

  private void OnProcessExited(object? sender, EventArgs e)
  {
    lock (_lock)
    {
      if (Status != BackendStatus.Stopped)
        SetError("Backend process exited unexpectedly.");
    }
  }

  private void SetStatus(BackendStatus status)
  {
    Status = status;
    StatusChanged?.Invoke();
  }

  private void SetError(string message)
  {
    LastError = message;
    AddLog($"❌ Error: {message}");
    SetStatus(BackendStatus.Error);
  }

  private void AddLog(string line)
  {
    lock (_lock) _logLines.Add(line);
    StatusChanged?.Invoke();
  }

  private static ProcessStartInfo BuildProcessStartInfo(string backendRoot)
  {
    // Prefer a local virtual-environment uvicorn over the system one
    string uvicornExe = FindUvicornExe(backendRoot);

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return new ProcessStartInfo
      {
        // Use cmd so the shell can resolve PATH; fall back to python -m uvicorn
        FileName = "cmd.exe",
        Arguments = $"/c \"{uvicornExe}\" app:app --port 8000",
        WorkingDirectory = backendRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
    }

    return new ProcessStartInfo
    {
      FileName = uvicornExe,
      Arguments = "app:app --port 8000",
      WorkingDirectory = backendRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
  }

  private static string FindUvicornExe(string backendRoot)
  {
    // Check for a .venv inside the backend root first
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      var venvExe = Path.Combine(backendRoot, ".venv", "Scripts", "uvicorn.exe");
      if (File.Exists(venvExe)) return venvExe;
    }
    else
    {
      var venvExe = Path.Combine(backendRoot, ".venv", "bin", "uvicorn");
      if (File.Exists(venvExe)) return venvExe;
    }

    // Fall back to whatever is on the system PATH
    return "uvicorn";
  }
}
