[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$DotnetRoot = $env:EXCELMCP_DEV_DOTNET_ROOT
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$serverDll = Join-Path $repoRoot "src\ExcelMcp.McpServer\bin\$Configuration\net10.0-windows\Sbroenne.ExcelMcp.McpServer.dll"

if (-not (Test-Path -LiteralPath $serverDll)) {
    throw "Development MCP build not found: $serverDll. Build the solution first."
}

$dotnetCommand = if (-not [string]::IsNullOrWhiteSpace($DotnetRoot)) {
    Join-Path $DotnetRoot 'dotnet.exe'
}
else {
    (Get-Command dotnet.exe -ErrorAction Stop).Source
}

if (-not (Test-Path -LiteralPath $dotnetCommand)) {
    throw "dotnet.exe not found: $dotnetCommand"
}

$env:DOTNET_ROOT = Split-Path -Parent $dotnetCommand
& $dotnetCommand $serverDll
exit $LASTEXITCODE
