using System.ComponentModel;
using System.Text.Json;
using Sbroenne.ExcelMcp.CLI.Infrastructure;
using Sbroenne.ExcelMcp.Core.Models;
using Sbroenne.ExcelMcp.Service;
using Spectre.Console.Cli;

namespace Sbroenne.ExcelMcp.CLI.Commands;

internal sealed class DataModelJobStartCommand : AsyncCommand<DataModelJobStartCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SessionId))
        {
            return CliErrorOutput.WriteError("Session ID is required.");
        }
        if (string.IsNullOrWhiteSpace(settings.UpdatesFile))
        {
            return CliErrorOutput.WriteError("Updates file is required.");
        }

        List<DataModelMeasureUpdate>? updates;
        try
        {
            var json = await File.ReadAllTextAsync(settings.UpdatesFile, cancellationToken);
            updates = JsonSerializer.Deserialize<List<DataModelMeasureUpdate>>(json, ServiceProtocol.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CliErrorOutput.WriteError($"Failed to read updates file: {ex.Message}");
        }

        if (updates == null || updates.Count == 0)
        {
            return CliErrorOutput.WriteError("Updates file must contain a non-empty JSON array.");
        }

        using var client = await DaemonAutoStart.EnsureAndConnectAsync(cancellationToken);
        var response = await client.SendAsync(new ServiceRequest
        {
            Command = "measurejob.start",
            SessionId = settings.SessionId,
            Args = JsonSerializer.Serialize(new
            {
                updates,
                saveOnCompletion = settings.SaveOnCompletion
            }, ServiceProtocol.JsonOptions),
            Source = "cli"
        }, cancellationToken);

        return WriteResponse(response);
    }

    private static int WriteResponse(ServiceResponse response)
    {
        if (!response.Success)
        {
            return CliErrorOutput.WriteServiceError(response);
        }

        Console.WriteLine(response.Result);
        return 0;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("-s|--session <SESSION>")]
        [Description("Session ID containing the open workbook")]
        public string SessionId { get; init; } = string.Empty;

        [CommandOption("--updates-file <FILE>")]
        [Description("UTF-8 JSON array of measure updates")]
        public string UpdatesFile { get; init; } = string.Empty;

        [CommandOption("--save")]
        [Description("Save the workbook after every update succeeds")]
        public bool SaveOnCompletion { get; init; }
    }
}

internal abstract class DataModelJobControlCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : DataModelJobControlCommand<TSettings>.SettingsBase
{
    protected abstract string Action { get; }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.OperationId))
        {
            return CliErrorOutput.WriteError("Operation ID is required.");
        }

        using var client = await DaemonAutoStart.EnsureAndConnectAsync(cancellationToken);
        var response = await client.SendAsync(new ServiceRequest
        {
            Command = $"measurejob.{Action}",
            Args = JsonSerializer.Serialize(
                new { operationId = settings.OperationId },
                ServiceProtocol.JsonOptions),
            Source = "cli"
        }, cancellationToken);

        if (!response.Success)
        {
            return CliErrorOutput.WriteServiceError(response);
        }

        Console.WriteLine(response.Result);
        return 0;
    }

    internal abstract class SettingsBase : CommandSettings
    {
        [CommandOption("-o|--operation <OPERATION>")]
        [Description("Operation ID returned by start")]
        public string OperationId { get; init; } = string.Empty;
    }
}

internal sealed class DataModelJobStatusCommand
    : DataModelJobControlCommand<DataModelJobStatusCommand.Settings>
{
    protected override string Action => "status";

    internal sealed class Settings : SettingsBase
    {
    }
}

internal sealed class DataModelJobCancelCommand
    : DataModelJobControlCommand<DataModelJobCancelCommand.Settings>
{
    protected override string Action => "cancel";

    internal sealed class Settings : SettingsBase
    {
    }
}
