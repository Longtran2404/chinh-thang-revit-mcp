# Chinh Thang Revit MCP local installer. Only touches this fork's installation.
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceDir,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'ChinhThangRevitMcp\app'),
    [string]$AddinsRoot = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'),
    [string]$RevitDir = 'C:\Program Files\Autodesk\Revit 2024',
    [ValidateSet('none','codex')][string]$Client = 'none',
    [string]$CodexConfigPath = (Join-Path $env:USERPROFILE '.codex\config.toml')
)
$ErrorActionPreference = 'Stop'
if (-not $SourceDir) {
    if (Test-Path "$PSScriptRoot\server") { $SourceDir = $PSScriptRoot }
    else { $SourceDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\ChinhThangRevitMcp-1.1.0-win-x64' }
}
if (-not (Test-Path "$RevitDir\RevitAPI.dll")) { throw 'Revit 2024 was not found.' }
foreach ($required in @('server\RvtMcp.Server.exe','plugin\RvtMcp.Plugin.dll','plugin\e_sqlite3.dll')) {
    if (-not (Test-Path (Join-Path $SourceDir $required))) { throw "Package is incomplete: $required" }
}
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before installing or updating the add-in.' }
if (Get-Process RvtMcp.Server -ErrorAction SilentlyContinue) { throw 'Disconnect the MCP server before updating its executable.' }
# Upstream and fork retain the same runtime protocol; do not load both in one Revit process.
if (Test-Path "$AddinsRoot\RvtMcp.R24.addin") { throw 'An upstream RvtMcp add-in is installed. Disable it before installing this fork.' }
$stamp = Get-Date -Format 'yyyyMMddHHmmssfff'
$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($PSCmdlet.ShouldProcess($InstallRoot, 'Back up existing application and install standalone server and plugin')) {
    if (Test-Path -LiteralPath $InstallRoot) { Copy-Item -LiteralPath $InstallRoot -Destination "$InstallRoot.backup-$stamp" -Recurse }
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    Copy-Item "$SourceDir\server", "$SourceDir\plugin" -Destination $InstallRoot -Recurse -Force
    Copy-Item "$SourceDir\LICENSE", "$SourceDir\NOTICE" -Destination $InstallRoot -Force
}
$manifest = Join-Path $AddinsRoot 'ChinhThangRevitMcp.addin'
$assembly = [System.Security.SecurityElement]::Escape((Join-Path $InstallRoot 'plugin\RvtMcp.Plugin.dll'))
$addin = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Chinh Thang Revit MCP</Name>
    <Assembly>$assembly</Assembly>
    <FullClassName>RvtMcp.Plugin.App</FullClassName>
    <AddInId>{4f52ea84-cc6e-4ce4-911b-e6657b74627d}</AddInId>
    <VendorId>CTMC</VendorId>
    <VendorDescription>Chinh Thang Revit MCP - based on bimwright/rvt-mcp</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
if ($PSCmdlet.ShouldProcess($manifest, 'Install Revit 2024 add-in manifest')) {
    New-Item -ItemType Directory -Path $AddinsRoot -Force | Out-Null
    if (Test-Path -LiteralPath $manifest) { Copy-Item -LiteralPath $manifest -Destination "$manifest.backup-$stamp" }
    [IO.File]::WriteAllText($manifest, $addin, $utf8)
}
if ($Client -eq 'codex') {
    $raw = if (Test-Path -LiteralPath $CodexConfigPath) { [IO.File]::ReadAllText($CodexConfigPath) } else { '' }
    $command = (Join-Path $InstallRoot 'server\RvtMcp.Server.exe').Replace('\','/')
    if ($command.Contains("'")) { throw 'Installation path cannot contain a single quote for the generated TOML literal.' }
    $block = @"
[mcp_servers.chinh-thang-revit-mcp]
command = '$command'
args = ["--target", "2024", "--toolsets", "all"]
startup_timeout_sec = 30
tool_timeout_sec = 120
"@
    $pattern = '(?ms)^\[mcp_servers\.chinh-thang-revit-mcp\][^\r\n]*\r?\n.*?(?=^\[|\z)'
    $matches = [regex]::Matches($raw, $pattern)
    if ($matches.Count -gt 1) { throw 'Duplicate MCP configuration; resolve it before installation.' }
    if ($matches.Count -eq 1) {
        $updated = [regex]::Replace($raw, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $block + "`r`n`r`n" })
    } else { $updated = $raw.TrimEnd() + "`r`n`r`n" + $block + "`r`n" }
    Write-Host "Proposed MCP entry:`n$block"
    if ($PSCmdlet.ShouldProcess($CodexConfigPath, 'Back up config and upsert only the chinh-thang-revit-mcp entry')) {
        New-Item -ItemType Directory -Path (Split-Path $CodexConfigPath -Parent) -Force | Out-Null
        if (Test-Path -LiteralPath $CodexConfigPath) { Copy-Item -LiteralPath $CodexConfigPath -Destination "$CodexConfigPath.ctmcp-backup-$stamp" }
        [IO.File]::WriteAllText($CodexConfigPath, $updated, $utf8)
    }
}
Write-Host 'Next: start Revit with a model, check Add-Ins > Chinh Thang MCP, then reconnect the MCP client.'
