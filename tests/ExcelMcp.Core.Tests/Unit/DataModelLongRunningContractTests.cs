using Sbroenne.ExcelMcp.Generated;
using Xunit;

namespace Sbroenne.ExcelMcp.Core.Tests.Unit;

[Trait("Layer", "Core")]
[Trait("Category", "Unit")]
[Trait("Feature", "DataModel")]
[Trait("Speed", "Fast")]
public sealed class DataModelLongRunningContractTests
{
    [Fact]
    public void DataModelContract_ExposesBatchMeasureUpdate()
    {
        Assert.Contains("update-measures", ServiceRegistry.DataModel.ValidActions);
    }
}
