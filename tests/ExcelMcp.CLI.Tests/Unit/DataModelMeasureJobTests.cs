using System.Text.Json;
using Sbroenne.ExcelMcp.ComInterop;
using Sbroenne.ExcelMcp.Core.Models;
using Sbroenne.ExcelMcp.Service;
using Xunit;

namespace Sbroenne.ExcelMcp.CLI.Tests.Unit;

[Trait("Layer", "Service")]
[Trait("Category", "Unit")]
[Trait("Feature", "DataModel")]
[Trait("Speed", "Fast")]
public sealed class DataModelMeasureJobTests
{
    [Fact]
    public async Task Status_UnknownOperation_ReturnsOperationNotFound()
    {
        using var service = new ExcelMcpService();

        var response = await service.ProcessAsync(new ServiceRequest
        {
            Command = "measurejob.status",
            Args = "{\"operationId\":\"missing\"}"
        });

        Assert.False(response.Success);
        Assert.Equal("OperationNotFound", response.ErrorCategory);
    }

    [Fact]
    public void Progress_TracksCurrentItemElapsedCountsAndSaveState()
    {
        using var job = new DataModelMeasureJob(
            "op-1",
            "session-1",
            1234,
            [
                new DataModelMeasureUpdate { MeasureName = "% Dados", DaxFormula = "1" },
                new DataModelMeasureUpdate { MeasureName = "% Video", DaxFormula = "1" }
            ],
            saveOnCompletion: true);

        job.MarkRunning();
        job.Report(new ProgressInfo
        {
            Current = 0,
            Total = 2,
            Message = "Updating measure '% Dados'"
        });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(job.CreateStatus(1)));
        var root = document.RootElement;
        Assert.Equal("running", root.GetProperty("state").GetString());
        Assert.Equal("% Dados", root.GetProperty("currentItem").GetString());
        Assert.Equal(0, root.GetProperty("completed").GetInt32());
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("activeOperations").GetInt32());
        Assert.Equal("pending", root.GetProperty("saveState").GetString());
        Assert.True(root.GetProperty("itemElapsedSeconds").GetDouble() >= 0);
        Assert.True(root.GetProperty("totalElapsedSeconds").GetDouble() >= 0);

        job.Report(new ProgressInfo
        {
            Current = 1,
            Total = 2,
            Message = "Updated measure '% Dados'"
        });
        job.RequestCancellation();
        job.RequestCancellation();

        using var cancelledDocument = JsonDocument.Parse(JsonSerializer.Serialize(job.CreateStatus(0)));
        Assert.Equal("cancelling", cancelledDocument.RootElement.GetProperty("state").GetString());
        Assert.Equal(1, cancelledDocument.RootElement.GetProperty("completed").GetInt32());
        Assert.True(job.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Failure_PreservesRootCauseAndReportsDiscardedChanges()
    {
        using var job = new DataModelMeasureJob(
            "op-2",
            "session-2",
            4567,
            [new DataModelMeasureUpdate { MeasureName = "% Dados", DaxFormula = "1" }],
            saveOnCompletion: true);
        job.MarkRunning();

        job.MarkFailedOrCancelled(new ServiceResponse
        {
            Success = false,
            ErrorCategory = "ComError",
            ErrorMessage = "Model setter failed",
            ExceptionType = "COMException",
            HResult = "0x800A03EC",
            InnerError = "Original Excel error"
        }, changesDiscarded: true);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(job.CreateStatus(0)));
        var root = document.RootElement;
        Assert.Equal("failed", root.GetProperty("state").GetString());
        Assert.Equal("discarded", root.GetProperty("saveState").GetString());
        Assert.Equal("ComError", root.GetProperty("errorCategory").GetString());
        Assert.Equal("Model setter failed", root.GetProperty("errorMessage").GetString());
        Assert.Equal("COMException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("0x800A03EC", root.GetProperty("hresult").GetString());
        Assert.Equal("Original Excel error", root.GetProperty("innerError").GetString());
    }

    [Fact]
    public void UnhandledFailure_ReportsWhetherPartialChangesWereDiscarded()
    {
        using var job = new DataModelMeasureJob(
            "op-3",
            "session-3",
            7890,
            [new DataModelMeasureUpdate { MeasureName = "% Dados", DaxFormula = "1" }],
            saveOnCompletion: true);
        job.MarkRunning();

        job.MarkFailed(new InvalidOperationException("Unexpected worker failure"), changesDiscarded: true);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(job.CreateStatus(0)));
        var root = document.RootElement;
        Assert.Equal("failed", root.GetProperty("state").GetString());
        Assert.Equal("discarded", root.GetProperty("saveState").GetString());
        Assert.Equal("Unexpected worker failure", root.GetProperty("errorMessage").GetString());
        Assert.Equal("InvalidOperationException", root.GetProperty("exceptionType").GetString());
    }
}
