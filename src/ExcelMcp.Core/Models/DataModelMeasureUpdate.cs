namespace Sbroenne.ExcelMcp.Core.Models;

/// <summary>One measure mutation in a batch Data Model update.</summary>
public sealed class DataModelMeasureUpdate
{
    /// <summary>Name of the existing measure.</summary>
    public string MeasureName { get; set; } = string.Empty;

    /// <summary>Optional new DAX formula.</summary>
    public string? DaxFormula { get; set; }

    /// <summary>Optional new format type.</summary>
    public string? FormatType { get; set; }

    /// <summary>Optional new description. Null preserves the current description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether to send this formula to daxformatter.com before saving.</summary>
    public bool FormatDax { get; set; }
}

/// <summary>Per-measure outcome from a batch update.</summary>
public sealed class DataModelMeasureUpdateOutcome
{
    /// <summary>Measure name.</summary>
    public string MeasureName { get; set; } = string.Empty;

    /// <summary>Whether at least one property was changed.</summary>
    public bool Updated { get; set; }

    /// <summary>Properties changed by the operation.</summary>
    public List<string> ChangedProperties { get; set; } = [];
}

/// <summary>Result from one request that updates multiple measures in one Excel batch.</summary>
public sealed class DataModelMeasureBatchUpdateResult : OperationResult
{
    /// <summary>Number of requested measures.</summary>
    public int Requested { get; set; }

    /// <summary>Number of measures changed.</summary>
    public int Updated { get; set; }

    /// <summary>Number of measures skipped because all requested values already matched.</summary>
    public int Unchanged { get; set; }

    /// <summary>Per-measure outcomes.</summary>
    public List<DataModelMeasureUpdateOutcome> Measures { get; set; } = [];
}
