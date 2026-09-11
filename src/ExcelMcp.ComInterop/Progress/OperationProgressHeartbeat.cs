using System.Diagnostics;

namespace Sbroenne.ExcelMcp.ComInterop;

/// <summary>
/// Emits periodic progress notifications while a synchronous COM call is in flight.
/// </summary>
public sealed class OperationProgressHeartbeat : IDisposable
{
    private readonly IProgress<ProgressInfo>? _progress;
    private readonly string _message;
    private readonly float _current;
    private readonly float? _total;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Timer? _timer;
    private int _disposed;

    /// <summary>Creates and starts a heartbeat reporter.</summary>
    public OperationProgressHeartbeat(
        IProgress<ProgressInfo>? progress,
        string message,
        float current,
        float? total,
        TimeSpan? interval = null)
    {
        _progress = progress;
        _message = message;
        _current = current;
        _total = total;

        if (progress != null)
        {
            progress.Report(new ProgressInfo { Current = current, Total = total, Message = message });
            var heartbeatInterval = interval ?? TimeSpan.FromSeconds(10);
            _timer = new Timer(ReportHeartbeat, null, heartbeatInterval, heartbeatInterval);
        }
    }

    private void ReportHeartbeat(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _progress?.Report(new ProgressInfo
            {
                Current = _current,
                Total = _total,
                Message = $"{_message} ({_stopwatch.Elapsed:hh\\:mm\\:ss} elapsed)"
            });
        }
        catch (Exception)
        {
            // Progress reporting must never fail the underlying Excel mutation.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer?.Dispose();
    }
}
