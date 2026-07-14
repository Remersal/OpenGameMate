[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
    $Version = [string]$buildProperties.Project.PropertyGroup.Version
}

if ([string]::IsNullOrWhiteSpace($Version) -or
    $Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Release version is missing or invalid: '$Version'"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release\OpenGameMate-v$Version-win-x64"
}

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
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

foreach ($fileName in @('README.md', 'LICENSE', 'PRIVACY.md', 'SECURITY.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $fileName) -Destination (Join-Path $output $fileName)
}

$releaseNotes = Join-Path $repositoryRoot "docs\RELEASE_NOTES_$Version.md"
if (-not (Test-Path -LiteralPath $releaseNotes -PathType Leaf)) {
    throw "Release notes not found for version ${Version}: $releaseNotes"
}

Copy-Item `
    -LiteralPath $releaseNotes `
    -Destination (Join-Path $output 'RELEASE_NOTES.md')

& (Join-Path $PSScriptRoot 'Validate-Privacy.ps1') -ArtifactPath $output

Write-Host "Portable package created: $output"
