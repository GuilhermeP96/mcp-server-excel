using System.ComponentModel;
using ModelContextProtocol.Server;
using Sbroenne.ExcelMcp.Core.Models;

namespace Sbroenne.ExcelMcp.McpServer.Tools;

/// <summary>Actions for asynchronous Power Pivot measure update jobs.</summary>
public enum DataModelJobAction
{
    /// <summary>Start a background measure update job.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("start")]
    Start,

    /// <summary>Read current job status and progress.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("status")]
    Status,

    /// <summary>Request cancellation of an active job.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("cancel")]
    Cancel
}

/// <summary>Asynchronous Data Model job MCP tool.</summary>
[McpServerToolType]
public static class ExcelDataModelJobTool
{
    /// <summary>
    /// Starts, monitors, or cancels a long-running batch of Power Pivot measure updates.
    /// Start returns immediately with operationId. Poll status without occupying one MCP
    /// tool call beyond the client timeout. cancel is idempotent. save_on_completion=true
    /// persists the workbook only after every measure succeeds.
    /// </summary>
    [McpServerTool(Name = "datamodel-job", Title = "Data Model Measure Job", Destructive = true)]
    [McpMeta("category", "analysis")]
    [McpMeta("requiresSession", false)]
    public static string ExcelDataModelJob(
        [Description("start, status, or cancel")] DataModelJobAction action,
        [DefaultValue(null)] string? session_id,
        [DefaultValue(null)] string? operation_id,
        [DefaultValue(null)] List<DataModelMeasureUpdate>? updates,
        [DefaultValue(false)] bool save_on_completion,
        CancellationToken cancellationToken = default)
    {
        using var cancellationScope = ExcelToolsBase.PushCancellationToken(cancellationToken);
        return ExcelToolsBase.ExecuteToolAction(
            "datamodel-job",
            action.ToString().ToLowerInvariant(),
            () => action switch
            {
                DataModelJobAction.Start => Start(session_id, updates, save_on_completion),
                DataModelJobAction.Status => Control("status", operation_id),
                DataModelJobAction.Cancel => Control("cancel", operation_id),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown job action")
            });
    }

    private static string Start(
        string? sessionId,
        List<DataModelMeasureUpdate>? updates,
        bool saveOnCompletion)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("session_id is required for start", nameof(sessionId));
        }
        if (updates == null || updates.Count == 0)
        {
            throw new ArgumentException("updates is required for start", nameof(updates));
        }

        return ExcelToolsBase.ForwardToService(
            "measurejob.start",
            sessionId,
            new { updates, saveOnCompletion });
    }

    private static string Control(string action, string? operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException($"operation_id is required for {action}", nameof(operationId));
        }

        return ExcelToolsBase.ForwardToServiceNoSession(
            $"measurejob.{action}",
            new { operationId });
    }
}
