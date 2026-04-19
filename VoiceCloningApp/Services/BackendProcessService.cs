using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
  private const int MaxLogLines = 200;

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

  /// <summary>
  /// Raised whenever status or log lines change.
  /// <para>
  /// <b>Threading:</b> handlers may be invoked on any thread — including
  /// background threadpool threads that deliver process stdout/stderr output.
  /// Blazor component subscribers must marshal back to the UI thread, e.g.:
  /// <code>
  /// _service.StatusChanged += async () => await InvokeAsync(StateHasChanged);
  /// </code>
  /// </para>
  /// </summary>
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
    // Capture state under lock; raise the event outside the lock to avoid
    // deadlocking subscribers that re-enter this service on the same thread.
    bool shouldStart;
    lock (_lock)
    {
      shouldStart = Status is not (BackendStatus.Starting or BackendStatus.Running);
      if (shouldStart)
      {
        _logLines.Clear();
        LastError = null;
        Status = BackendStatus.Starting;
      }
    }

    if (!shouldStart) return;
    StatusChanged?.Invoke();

    _startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    try
    {
      var psi = BuildProcessStartInfo(backendRoot);
      _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

      _process.OutputDataReceived += (_, e) =>
      {
        if (e.Data is null) return;
        AddLog(e.Data);
      };

      _process.ErrorDataReceived += (_, e) =>
      {
        if (e.Data is null) return;
        AddLog($"[err] {e.Data}");
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

      // Kill the orphaned process so it doesn't linger while the UI shows an
      // error — otherwise uvicorn would hold port 8000 until the app restarts.
      KillAndDisposeProcess();
    }
    catch (OperationCanceledException)
    {
      KillAndDisposeProcess();
      SetError("Startup was cancelled.");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start backend process.");
      KillAndDisposeProcess();
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

    string logEntry;
    lock (_lock)
    {
      logEntry = "Backend process stopped.";
      try
      {
        if (_process is { HasExited: false })
          _process.Kill(entireProcessTree: true);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error while stopping backend process.");
        logEntry = $"[err] Stop error: {ex.Message}";
      }
      finally
      {
        _process?.Dispose();
        _process = null;
        _logLines.Add(logEntry);
        Status = BackendStatus.Stopped;
      }
    }

    // Notify outside the lock to avoid deadlocking subscribers.
    StatusChanged?.Invoke();
  }

  public void Dispose() => Stop();

  // -------------------------------------------------------------------------
  // Private helpers
  // -------------------------------------------------------------------------

  private void OnProcessExited(object? sender, EventArgs e)
  {
    bool shouldNotify;
    lock (_lock)
    {
      shouldNotify = Status != BackendStatus.Stopped;
      if (shouldNotify)
      {
        LastError = "Backend process exited unexpectedly.";
        _logLines.Add($"❌ Error: {LastError}");
        Status = BackendStatus.Error;
      }
    }

    // Raise outside the lock to avoid deadlocking subscribers.
    if (shouldNotify) StatusChanged?.Invoke();
  }

  private void SetStatus(BackendStatus status)
  {
    // Callers of SetStatus must NOT hold _lock when calling this method,
    // since StatusChanged subscribers may re-enter the service.
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
    if (string.IsNullOrWhiteSpace(line)) return;

    lock (_lock)
    {
      _logLines.Add(line);
      if (_logLines.Count > MaxLogLines)
      {
        _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
      }
    }

    StatusChanged?.Invoke();
  }

  /// <summary>
  /// Kills and disposes <see cref="_process"/> when it is still running.
  /// Safe to call from any thread; no lock held on entry or exit.
  /// </summary>
  private void KillAndDisposeProcess()
  {
    Process? proc;
    lock (_lock)
    {
      proc = _process;
      _process = null;
    }

    if (proc is null) return;

    try
    {
      if (!proc.HasExited)
        proc.Kill(entireProcessTree: true);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error while killing backend process.");
    }
    finally
    {
      proc.Dispose();
    }
  }

  private static ProcessStartInfo BuildProcessStartInfo(string backendRoot)
  {
    var psi = new ProcessStartInfo
    {
      FileName = FindPythonExe(backendRoot),
      Arguments = "-m uvicorn app:app --host 127.0.0.1 --port 8000",
      WorkingDirectory = backendRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
      UseShellExecute = false,
      CreateNoWindow = true,
    };

    psi.Environment["PYTHONUTF8"] = "1";
    psi.Environment["PYTHONIOENCODING"] = "utf-8";
    psi.Environment["PYTHONUNBUFFERED"] = "1";
    psi.Environment["HF_HUB_DISABLE_PROGRESS_BARS"] = "1";
    psi.Environment["NO_COLOR"] = "1";

    return psi;
  }

  /// <summary>
  /// Returns the venv Python executable when present; otherwise the system
  /// <c>python</c> (Windows) or <c>python3</c> (macOS / Linux) from PATH.
  /// </summary>
  private static string FindPythonExe(string backendRoot)
  {
    var (venvExe, systemExe) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? (Path.Combine(backendRoot, ".venv", "Scripts", "python.exe"), "python")
        : (Path.Combine(backendRoot, ".venv", "bin", "python"), "python3");
    return File.Exists(venvExe) ? venvExe : systemExe;
  }
}
