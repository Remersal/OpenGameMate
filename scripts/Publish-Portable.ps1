[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\release\OpenGameMate-v0.1.0-win-x64')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $output) {
    $existing = Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1
    if ($null -ne $existing) {
        throw "Output directory is not empty. Remove its contents manually, then run again: $output"
    }
}

$dotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

& $dotnet publish (Join-Path $repositoryRoot 'src\OpenGameMate.App\OpenGameMate.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $output `
    -p:Version=0.1.0 `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

foreach ($fileName in @('README.md', 'LICENSE', 'PRIVACY.md', 'SECURITY.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $fileName) -Destination (Join-Path $output $fileName)
}

Write-Host "Portable package created: $output"
