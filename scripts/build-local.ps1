# Chinh Thang Revit MCP source build and portable package.
#Requires -Version 5.1
[CmdletBinding()]
param([string]$Dotnet, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Dotnet) {
    $found = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($found) { $Dotnet = $found.Source }
    else { $Dotnet = Join-Path $env:LOCALAPPDATA 'ChinhThangRevitMcp\sdk\dotnet.exe' }
}
if (-not (Test-Path -LiteralPath $Dotnet)) { throw 'Install the .NET 8 SDK or pass -Dotnet with its absolute path.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function Invoke-Dotnet([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}
Push-Location $root
try {
    $package = Join-Path $root 'artifacts\ChinhThangRevitMcp-1.0.0-win-x64'
    if (Test-Path -LiteralPath $package) {
        # Do not mix old and new build outputs; keep the previous package intact.
        Move-Item -LiteralPath $package -Destination ($package + '.previous-' + (Get-Date -Format 'yyyyMMddHHmmssfff'))
    }
    if (-not $SkipTests) {
        Invoke-Dotnet @('test', 'tests/RvtMcp.Tests/RvtMcp.Tests.csproj', '-c', 'Release', '--nologo', '--logger', 'trx', '--results-directory', 'artifacts/test-results')
    }
    Invoke-Dotnet @('build', 'src/plugin-r24/RvtMcp.Plugin.R24.csproj', '-c', 'Release', '--nologo', '/p:RvtMcpSkipDeploy=true')
    Invoke-Dotnet @('publish', 'src/server/RvtMcp.Server.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', "$package\server", '--nologo')
    $plugin = New-Item -ItemType Directory -Path "$package\plugin" -Force
    Get-ChildItem 'src/plugin-r24/bin/Release/net48' -File | Where-Object {
        $_.Extension -eq '.dll' -and $_.Name -notmatch '^RevitAPI(UI)?\.dll$'
    } | Copy-Item -Destination $plugin.FullName
    $native = 'src/plugin-r24/bin/Release/net48/runtimes/win-x64/native/e_sqlite3.dll'
    if (-not (Test-Path $native)) { throw 'Missing native SQLite dependency.' }
    Copy-Item -LiteralPath $native -Destination $plugin.FullName
    Copy-Item 'scripts/install-local.ps1', 'scripts/smoke-test.py', 'LICENSE', 'NOTICE', 'FORK_CHANGES.md', 'LOCAL_SETUP.md' -Destination $package
    $hashes = Get-ChildItem $package -Recurse -File | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($package.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$package\checksums.json" -Encoding UTF8
    Compress-Archive -Path "$package\*" -DestinationPath "$package.zip" -Force
    Write-Host "Package ready: $package.zip"
} finally { Pop-Location }
