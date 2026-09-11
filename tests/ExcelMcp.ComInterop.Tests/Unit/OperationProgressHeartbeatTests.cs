using Xunit;

namespace Sbroenne.ExcelMcp.ComInterop.Tests.Unit;

[Trait("Layer", "ComInterop")]
[Trait("Category", "Unit")]
[Trait("Speed", "Fast")]
public sealed class OperationProgressHeartbeatTests
{
    [Fact]
    public async Task Heartbeat_ReportsInitialAndPeriodicProgress()
    {
        var reports = new System.Collections.Concurrent.ConcurrentQueue<Sbroenne.ExcelMcp.ComInterop.ProgressInfo>();
        var progress = new InlineProgress<Sbroenne.ExcelMcp.ComInterop.ProgressInfo>(reports.Enqueue);

        using (new Sbroenne.ExcelMcp.ComInterop.OperationProgressHeartbeat(
                   progress,
                   "Updating measure 'Ticket'",
                   1,
                   4,
                   TimeSpan.FromMilliseconds(20)))
        {
            await Task.Delay(90);
        }

        Assert.True(reports.Count >= 2);
        Assert.Equal(1, reports.First().Current);
        Assert.Equal(4, reports.First().Total);
        Assert.Contains(reports, report =>
            report.Message?.Contains("elapsed", StringComparison.Ordinal) == true);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
