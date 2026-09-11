using Xunit;
using Xunit.Abstractions;

namespace Sbroenne.ExcelMcp.McpServer.Tests.Integration.Tools;

[Collection("ProgramTransport")]
[Trait("Category", "Integration")]
[Trait("Speed", "Fast")]
[Trait("Layer", "McpServer")]
[Trait("Feature", "DataModel")]
[Trait("RequiresExcel", "false")]
public sealed class DataModelJobContractProtocolTests : McpIntegrationTestBase
{
    public DataModelJobContractProtocolTests(ITestOutputHelper output)
        : base(output, "DataModelJobContractProtocolClient")
    {
    }

    [Fact]
    public async Task ListTools_DataModelJobSchema_ExposesStartStatusCancel()
    {
        var tools = await Client!.ListToolsAsync(cancellationToken: TestCancellationToken);
        var tool = Assert.Single(tools, candidate => candidate.Name == "datamodel-job");
        var actions = tool.JsonSchema
            .GetProperty("properties")
            .GetProperty("action")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        Assert.Equal(["start", "status", "cancel"], actions);
    }
}
