# Long-running Data Model operations

Large Power Pivot models can spend several minutes compiling a single measure.
`datamodel update-measures` reduces avoidable overhead by updating a list of
measures through one workbook session and one Data Model acquisition. Formulas
and descriptions that already match are skipped, so they do not trigger another
model compilation.

```json
{
  "updates": [
    { "measureName": "% Data", "daxFormula": "DIVIDE([Data value], [Total])" },
    { "measureName": "% Voice", "daxFormula": "DIVIDE([Voice value], [Total])" }
  ]
}
```

The operation reports a start notification, a heartbeat every ten seconds while
Excel is inside the synchronous COM setter, and a completion notification for
each measure. Cancellation from the MCP client now reaches the Excel STA queue
and OLE message filter. If a client disconnects or times out, the session is
closed instead of leaving work queued with a stale active-operation counter.

For models that take longer than the MCP client's request limit, use the
`datamodel-job` tool. `start` returns an `operationId` immediately. Poll `status`
to observe `currentItem`, `completed`, `total`, item and total elapsed time,
Excel process ID, active-operation count, save state, and the root error fields.
`cancel` is idempotent and requests cooperative cancellation without blocking
the status channel. With `save_on_completion=true`, the workbook is saved only
after every requested update succeeds.
If an update fails or the job is cancelled, the job closes its session without
saving so partially applied in-memory measure changes are discarded.
Job state lives in the owning MCP/CLI service process and completed jobs remain
queryable there for at least one hour. Jobs cannot resume after that process is
stopped because Excel COM sessions are process-bound.

The CLI exposes the same lifecycle. Put the update array in a UTF-8 JSON file,
then keep polling the returned operation ID:

```powershell
excelcli datamodel-job start --session $sessionId --updates-file .\measures.json --save
excelcli datamodel-job status --operation $operationId
excelcli datamodel-job cancel --operation $operationId
```

Use `file` with `action=cancel` for an explicit emergency stop. It force-closes
the session and discards unsaved changes. Reopen the workbook before retrying.

The Excel COM API still compiles each changed measure separately; it has no
public transaction that commits multiple DAX formula changes with one compile.
TOM can batch metadata and call `Model.SaveChanges()` once against an exposed
Analysis Services endpoint, but an Excel workbook advertises
`Data Source=$Embedded$`. That token is provider-internal: it does not expose a server name,
port, or attachable XMLA endpoint to external TOM/AMO clients. The asynchronous
job therefore makes the unavoidable Excel COM work observable and cancellable;
it does not claim to reduce Excel's per-setter model compilation cost.

## Side-by-side development server

Build the solution, then register a distinct development alias without replacing
the installed server:

```powershell
$env:EXCELMCP_DEV_DOTNET_ROOT = 'C:\path\to\dotnet-10'
$cloneRoot = 'C:\path\to\mcp-server-excel'
codex mcp add excel-mcp-dev --env EXCELMCP_DEV_DOTNET_ROOT=$env:EXCELMCP_DEV_DOTNET_ROOT -- `
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$cloneRoot\scripts\Start-ExcelMcpDev.ps1"
```

For VS Code, add the same launcher under a distinct `excel-mcp-dev` server name,
then run **Developer: Reload Window**. Keep the original `excel-mcp` entry intact.

## Preserve VBA helper errors

Capture the original VBA error before cleanup. Cleanup statements can overwrite
`Err`, making a final `Err.Raise` report only the helper's wrapper line.

```vb
Fail:
    originalNumber = Err.Number
    originalSource = Err.Source
    originalDescription = Err.Description
    On Error Resume Next
    ' cleanup
    On Error GoTo 0
    Err.Raise originalNumber, "RefreshDomV18 > " & originalSource, originalDescription
```
