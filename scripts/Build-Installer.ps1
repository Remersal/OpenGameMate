[CmdletBinding()]
param(
    [string]$InnoCompiler,
    [string]$PortableOutputDirectory,
    [string]$InstallerOutputDirectory,
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

if ([string]::IsNullOrWhiteSpace($PortableOutputDirectory)) {
    $PortableOutputDirectory = Join-Path $repositoryRoot "artifacts\release\OpenGameMate-v$Version-win-x64"
}

if ([string]::IsNullOrWhiteSpace($InstallerOutputDirectory)) {
    $InstallerOutputDirectory = Join-Path $repositoryRoot 'artifacts\installer'
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'Inno Setup 6 is not installed or ISCC.exe is not on PATH. Install it, then rerun with -InnoCompiler if needed.'
    }

    $InnoCompiler = $command.Source
}

if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup compiler not found: $InnoCompiler"
}

$portableOutput = [System.IO.Path]::GetFullPath($PortableOutputDirectory)
$installerOutput = [System.IO.Path]::GetFullPath($InstallerOutputDirectory)

if (Test-Path -LiteralPath $installerOutput) {
    $existingInstaller = Get-ChildItem -LiteralPath $installerOutput -Force | Select-Object -First 1
    if ($null -ne $existingInstaller) {
        throw "Installer output directory is not empty. Choose a new empty directory, then run again: $installerOutput"
    }
}

& (Join-Path $PSScriptRoot 'Publish-Portable.ps1') `
    -OutputDirectory $portableOutput `
    -Version $Version

& $InnoCompiler `
    "/DMyAppVersion=$Version" `
    "/DMySourceDirectory=$portableOutput" `
    "/DMyInstallerOutputDirectory=$installerOutput" `
    (Join-Path $repositoryRoot 'packaging\OpenGameMate.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

Write-Host "Installer package created: $installerOutput"
