using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Sbroenne.ExcelMcp.ComInterop;
using Sbroenne.ExcelMcp.Core.Models;
using Sbroenne.ExcelMcp.Generated;

namespace Sbroenne.ExcelMcp.Service;

public sealed partial class ExcelMcpService
{
    private readonly ConcurrentDictionary<string, DataModelMeasureJob> _measureJobs =
        new(StringComparer.Ordinal);

    private ServiceResponse HandleMeasureJobCommand(string action, ServiceRequest request)
    {
        return action switch
        {
            "start" => StartMeasureJob(request),
            "status" => GetMeasureJobStatus(request),
            "cancel" => CancelMeasureJob(request),
            _ => new ServiceResponse
            {
                Success = false,
                ErrorCategory = "InvalidInput",
                ErrorMessage = $"Unknown measure job action: {action}"
            }
        };
    }

    private ServiceResponse StartMeasureJob(ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return InvalidMeasureJobRequest("sessionId is required");
        }

        var args = JsonSerializer.Deserialize<DataModelMeasureJobStartArgs>(
            request.Args ?? "{}",
            ServiceProtocol.JsonOptions) ?? new DataModelMeasureJobStartArgs();

        if (args.Updates.Count == 0)
        {
            return InvalidMeasureJobRequest("updates must contain at least one measure");
        }

        if (_measureJobs.Values.Any(job =>
                string.Equals(job.SessionId, request.SessionId, StringComparison.Ordinal)
                && !job.IsTerminal))
        {
            return InvalidMeasureJobRequest(
                $"Session '{request.SessionId}' already has an active measure update job.");
        }

        var batch = _sessionManager.GetSession(request.SessionId);
        if (batch == null)
        {
            return new ServiceResponse
            {
                Success = false,
                ErrorCategory = "SessionNotFound",
                ErrorMessage = $"Session '{request.SessionId}' not found"
            };
        }

        var innerArgs = JsonSerializer.Serialize(
            new { updates = args.Updates },
            ServiceProtocol.JsonOptions);
        ServiceRegistry.ValidateCommandArguments("datamodel.update-measures", innerArgs);

        PruneMeasureJobs();
        var operationId = Guid.NewGuid().ToString("N");
        var job = new DataModelMeasureJob(
            operationId,
            request.SessionId,
            batch.ExcelProcessId,
            args.Updates,
            args.SaveOnCompletion);
        if (!_measureJobs.TryAdd(operationId, job))
        {
            throw new InvalidOperationException("Failed to register measure update job.");
        }

        _ = Task.Run(() => RunMeasureJobAsync(job, innerArgs));
        return MeasureJobResponse(job);
    }

    private async Task RunMeasureJobAsync(DataModelMeasureJob job, string innerArgs)
    {
        job.MarkRunning();
        var previousProgress = ProgressContext.Current;
        ProgressContext.Current = new DataModelMeasureJobProgress(job);
        try
        {
            var response = await ProcessAsync(new ServiceRequest
            {
                Command = "datamodel.update-measures",
                SessionId = job.SessionId,
                Args = innerArgs,
                Source = "measure-job"
            }, job.CancellationToken).ConfigureAwait(false);

            if (!response.Success)
            {
                job.MarkFailedOrCancelled(response, TryDiscardMeasureJobSession(job.SessionId));
                return;
            }

            job.SetResult(response.Result);
            if (job.SaveOnCompletion)
            {
                job.MarkSaving();
                var saveResponse = await WithSessionAsync(job.SessionId, batch =>
                {
                    batch.Save(job.CancellationToken);
                    return new ServiceResponse { Success = true };
                }).ConfigureAwait(false);

                if (!saveResponse.Success)
                {
                    job.MarkFailedOrCancelled(saveResponse, TryDiscardMeasureJobSession(job.SessionId));
                    return;
                }

                job.MarkSaved();
            }

            job.MarkCompleted();
        }
        catch (Exception ex)
        {
            job.MarkFailed(ex, TryDiscardMeasureJobSession(job.SessionId));
        }
        finally
        {
            ProgressContext.Current = previousProgress;
            job.CompleteCleanup();
        }
    }

    private ServiceResponse GetMeasureJobStatus(ServiceRequest request)
    {
        var args = JsonSerializer.Deserialize<DataModelMeasureJobControlArgs>(
            request.Args ?? "{}",
            ServiceProtocol.JsonOptions) ?? new DataModelMeasureJobControlArgs();
        if (string.IsNullOrWhiteSpace(args.OperationId))
        {
            return InvalidMeasureJobRequest("operationId is required");
        }

        return _measureJobs.TryGetValue(args.OperationId, out var job)
            ? MeasureJobResponse(job)
            : new ServiceResponse
            {
                Success = false,
                ErrorCategory = "OperationNotFound",
                ErrorMessage = $"Measure update job '{args.OperationId}' not found"
            };
    }

    private bool TryDiscardMeasureJobSession(string sessionId)
    {
        try
        {
            _sessionManager.CloseSession(sessionId, save: false, force: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ServiceResponse CancelMeasureJob(ServiceRequest request)
    {
        var args = JsonSerializer.Deserialize<DataModelMeasureJobControlArgs>(
            request.Args ?? "{}",
            ServiceProtocol.JsonOptions) ?? new DataModelMeasureJobControlArgs();
        if (string.IsNullOrWhiteSpace(args.OperationId))
        {
            return InvalidMeasureJobRequest("operationId is required");
        }

        if (!_measureJobs.TryGetValue(args.OperationId, out var job))
        {
            return new ServiceResponse
            {
                Success = false,
                ErrorCategory = "OperationNotFound",
                ErrorMessage = $"Measure update job '{args.OperationId}' not found"
            };
        }

        job.RequestCancellation();
        return MeasureJobResponse(job);
    }

    private ServiceResponse MeasureJobResponse(DataModelMeasureJob job)
    {
        var status = job.CreateStatus(
            _sessionManager.GetActiveOperationCount(job.SessionId));
        return new ServiceResponse
        {
            Success = true,
            SessionId = job.SessionId,
            Result = JsonSerializer.Serialize(status, ServiceProtocol.JsonOptions)
        };
    }

    private static ServiceResponse InvalidMeasureJobRequest(string message) => new()
    {
        Success = false,
        ErrorCategory = "InvalidInput",
        ErrorMessage = message
    };

    private void PruneMeasureJobs()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
        foreach (var job in _measureJobs.Values
                     .Where(item => item.IsTerminal && item.CompletedAtUtc < cutoff)
                     .OrderBy(item => item.CompletedAtUtc)
                     .ToList())
        {
            _measureJobs.TryRemove(job.OperationId, out _);
        }
    }

    private void CancelMeasureJobs()
    {
        foreach (var job in _measureJobs.Values)
        {
            job.RequestCancellation();
        }
    }
}

internal sealed class DataModelMeasureJob : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();
    private Stopwatch? _itemStopwatch;
    private int _cleanupCompleted;
    private string _state = "queued";
    private string? _currentItem;
    private int _completed;
    private string _saveState;
    private string? _message;
    private string? _errorCategory;
    private string? _errorMessage;
    private string? _exceptionType;
    private string? _hresult;
    private string? _innerError;
    private string? _result;

    public DataModelMeasureJob(
        string operationId,
        string sessionId,
        int? excelProcessId,
        IReadOnlyList<DataModelMeasureUpdate> updates,
        bool saveOnCompletion)
    {
        OperationId = operationId;
        SessionId = sessionId;
        ExcelProcessId = excelProcessId;
        Total = updates.Count;
        SaveOnCompletion = saveOnCompletion;
        _saveState = saveOnCompletion ? "pending" : "not-requested";
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OperationId { get; }
    public string SessionId { get; }
    public int? ExcelProcessId { get; }
    public int Total { get; }
    public bool SaveOnCompletion { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? CompletedAtUtc { get; private set; }
    public CancellationToken CancellationToken => _cancellation.Token;

    public bool IsTerminal
    {
        get
        {
            lock (_sync)
            {
                return _state is "completed" or "failed" or "cancelled";
            }
        }
    }

    public void MarkRunning()
    {
        lock (_sync)
        {
            _state = "running";
            _message = "Measure update job started.";
        }
    }

    public void Report(ProgressInfo progress)
    {
        lock (_sync)
        {
            _message = progress.Message;
            string? item = ExtractMeasureName(progress.Message);
            if (item != null && !string.Equals(item, _currentItem, StringComparison.Ordinal))
            {
                _currentItem = item;
                _itemStopwatch = Stopwatch.StartNew();
            }

            if (progress.Message?.StartsWith("Updated measure", StringComparison.Ordinal) == true
                || progress.Message?.StartsWith("Skipped unchanged measure", StringComparison.Ordinal) == true)
            {
                _completed = Math.Max(_completed, Convert.ToInt32(progress.Current));
                _itemStopwatch?.Stop();
            }
        }
    }

    public void SetResult(string? result)
    {
        lock (_sync)
        {
            _result = result;
            _completed = Total;
            _currentItem = null;
            _itemStopwatch = null;
        }
    }

    public void MarkSaving()
    {
        lock (_sync)
        {
            _state = "saving";
            _saveState = "saving";
            _message = "Saving workbook changes.";
            _currentItem = null;
            _itemStopwatch = null;
        }
    }

    public void MarkSaved()
    {
        lock (_sync)
        {
            _saveState = "saved";
        }
    }

    public void MarkCompleted()
    {
        lock (_sync)
        {
            _state = "completed";
            _message = SaveOnCompletion
                ? "Measure updates completed and workbook saved."
                : "Measure updates completed; workbook has not been saved.";
            CompleteTiming();
        }
    }

    public void MarkFailedOrCancelled(ServiceResponse response, bool changesDiscarded)
    {
        lock (_sync)
        {
            bool cancelled = _cancellation.IsCancellationRequested
                || string.Equals(response.ErrorCategory, "Cancelled", StringComparison.OrdinalIgnoreCase);
            _state = cancelled ? "cancelled" : "failed";
            if (_saveState != "saved")
            {
                _saveState = changesDiscarded ? "discarded" : "not-saved";
            }
            _errorCategory = response.ErrorCategory;
            _errorMessage = response.ErrorMessage;
            _exceptionType = response.ExceptionType;
            _hresult = response.HResult;
            _innerError = response.InnerError;
            _message = cancelled
                ? "Measure update job cancelled."
                : "Measure update job failed.";
            CompleteTiming();
        }
    }

    public void MarkFailed(Exception exception, bool changesDiscarded)
    {
        lock (_sync)
        {
            _state = _cancellation.IsCancellationRequested ? "cancelled" : "failed";
            if (_saveState != "saved")
            {
                _saveState = changesDiscarded ? "discarded" : "not-saved";
            }
            _errorCategory = _cancellation.IsCancellationRequested ? "Cancelled" : "Unhandled";
            _errorMessage = exception.Message;
            _exceptionType = exception.GetType().Name;
            _innerError = exception.InnerException?.Message;
            _message = _state == "cancelled"
                ? "Measure update job cancelled."
                : "Measure update job failed.";
            CompleteTiming();
        }
    }

    public void RequestCancellation()
    {
        lock (_sync)
        {
            if (IsTerminal)
            {
                return;
            }

            _state = "cancelling";
            _message = "Cancellation requested; waiting for Excel COM to return.";
            _cancellation.Cancel();
        }
    }

    public void CompleteCleanup()
    {
        if (Interlocked.Exchange(ref _cleanupCompleted, 1) == 0)
        {
            _cancellation.Dispose();
        }
    }

    public void Dispose() => CompleteCleanup();

    public object CreateStatus(int activeOperations)
    {
        lock (_sync)
        {
            return new
            {
                success = true,
                operationId = OperationId,
                state = _state,
                sessionId = SessionId,
                excelProcessId = ExcelProcessId,
                currentItem = _currentItem,
                completed = _completed,
                total = Total,
                itemElapsedSeconds = _itemStopwatch == null
                    ? (double?)null
                    : Math.Round(_itemStopwatch.Elapsed.TotalSeconds, 3),
                totalElapsedSeconds = Math.Round(_totalStopwatch.Elapsed.TotalSeconds, 3),
                activeOperations,
                saveOnCompletion = SaveOnCompletion,
                saveState = _saveState,
                message = _message,
                errorCategory = _errorCategory,
                errorMessage = _errorMessage,
                exceptionType = _exceptionType,
                hresult = _hresult,
                innerError = _innerError,
                result = _result,
                createdAtUtc = CreatedAtUtc,
                completedAtUtc = CompletedAtUtc
            };
        }
    }

    private void CompleteTiming()
    {
        _itemStopwatch?.Stop();
        _totalStopwatch.Stop();
        CompletedAtUtc ??= DateTime.UtcNow;
    }

    private static string? ExtractMeasureName(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        int firstQuote = message.IndexOf('\'');
        int lastQuote = message.LastIndexOf('\'');
        return firstQuote >= 0 && lastQuote > firstQuote
            ? message[(firstQuote + 1)..lastQuote]
            : null;
    }
}

internal sealed class DataModelMeasureJobProgress(DataModelMeasureJob job) : IProgress<ProgressInfo>
{
    public void Report(ProgressInfo value) => job.Report(value);
}

public sealed class DataModelMeasureJobStartArgs
{
    public List<DataModelMeasureUpdate> Updates { get; set; } = [];
    public bool SaveOnCompletion { get; set; }
}

public sealed class DataModelMeasureJobControlArgs
{
    public string? OperationId { get; set; }
}
