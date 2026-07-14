[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildPropertiesPath = Join-Path $repositoryRoot 'Directory.Build.props'
[xml]$buildProperties = Get-Content -LiteralPath $buildPropertiesPath -Raw

function Get-BuildProperty {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $values = @(
        $buildProperties.Project.PropertyGroup |
            ForEach-Object { $_.$Name } |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
    )
    if ($values.Count -ne 1) {
        throw "Directory.Build.props must define exactly one $Name value."
    }

    return [string]$values[0]
}

$version = Get-BuildProperty -Name 'Version'
$assemblyVersion = Get-BuildProperty -Name 'AssemblyVersion'
$fileVersion = Get-BuildProperty -Name 'FileVersion'
$informationalVersion = Get-BuildProperty -Name 'InformationalVersion'

if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Release version is invalid: '$version'"
}

$numericVersion = $version.Split('-', 2)[0]
if ($assemblyVersion -ne "$numericVersion.0" -or $fileVersion -ne "$numericVersion.0") {
    throw "AssemblyVersion and FileVersion must both match $numericVersion.0."
}

if ($informationalVersion -ne '$(Version)') {
    throw 'InformationalVersion must be sourced from $(Version).'
}

$manifestPath = Join-Path $repositoryRoot 'src\OpenGameMate.App\app.manifest'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('asm', 'urn:schemas-microsoft-com:asm.v1')
$identity = $manifest.SelectSingleNode('/asm:assembly/asm:assemblyIdentity', $namespaceManager)
if ($null -eq $identity -or $identity.GetAttribute('version') -ne $assemblyVersion) {
    throw "app.manifest assembly identity must match AssemblyVersion $assemblyVersion."
}

$releaseNotes = Join-Path $repositoryRoot "docs\RELEASE_NOTES_$version.md"
if (-not (Test-Path -LiteralPath $releaseNotes -PathType Leaf)) {
    throw "Release notes are missing for version $version."
}

$powerShellScripts = @(
    (Join-Path $repositoryRoot 'scripts\Publish-Portable.ps1'),
    (Join-Path $repositoryRoot 'scripts\Build-Installer.ps1')
)
foreach ($scriptPath in $powerShellScripts) {
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        $messages = $parseErrors.Message -join '; '
        throw "PowerShell syntax is invalid in $scriptPath`: $messages"
    }

    $scriptText = Get-Content -LiteralPath $scriptPath -Raw
    if ($scriptText -match '(?i)Remove-Item[^\r\n]*-Recurse|\brm\s+-rf\b|\brmdir\s+/s\b|\brd\s+/s\b|\bdel\s+/s\b') {
        throw "Release script contains a forbidden recursive delete command: $scriptPath"
    }
}

$innoDefinition = Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging\OpenGameMate.iss') -Raw
foreach ($requiredFragment in @(
    '#ifndef MyAppVersion',
    'OutputBaseFilename=OpenGameMate-Setup-{#MyAppVersion}',
    'OutputDir={#MyInstallerOutputDirectory}',
    'Source: "{#MySourceDirectory}\*"'
)) {
    if ($innoDefinition.IndexOf($requiredFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Inno Setup definition is missing required release macro usage: $requiredFragment"
    }
}

Write-Host "Release metadata is consistent for OpenGameMate $version."
