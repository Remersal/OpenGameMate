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
$pathMap = Get-BuildProperty -Name 'PathMap'

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

if ($pathMap -ne '$(MSBuildThisFileDirectory)=/_/') {
    throw 'PathMap must map the repository root to the virtual /_/ source path.'
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

$applicationIcon = Join-Path $repositoryRoot 'assets\OpenGameMate.AppIcon.ico'
if (-not (Test-Path -LiteralPath $applicationIcon -PathType Leaf)) {
    throw "Application icon is missing: $applicationIcon"
}

$simplifiedChineseMessages = Join-Path $repositoryRoot 'packaging\Languages\ChineseSimplified.isl'
if (-not (Test-Path -LiteralPath $simplifiedChineseMessages -PathType Leaf)) {
    throw "Simplified Chinese installer messages are missing: $simplifiedChineseMessages"
}

$simplifiedChineseDefinition = Get-Content -LiteralPath $simplifiedChineseMessages -Raw
foreach ($requiredFragment in @(
    'LanguageName=',
    'LanguageID=$0804',
    'CreateDesktopIcon=',
    'LaunchProgram='
)) {
    if ($simplifiedChineseDefinition.IndexOf($requiredFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Simplified Chinese installer messages are missing required metadata: $requiredFragment"
    }
}

$simplifiedChineseHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $simplifiedChineseMessages).Hash
if ($simplifiedChineseHash -ne '6753BE2C5E2740D859900FD902824DB2EC568DA5C5B52486524C9762D778B0B0') {
    throw "Simplified Chinese installer messages do not match the reviewed translation: $simplifiedChineseHash"
}

[xml]$appProject = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\OpenGameMate.App\OpenGameMate.App.csproj') -Raw
$applicationIconProperty = @(
    $appProject.Project.PropertyGroup |
        ForEach-Object { $_.ApplicationIcon } |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
)
if ($applicationIconProperty.Count -ne 1 -or
    [string]$applicationIconProperty[0] -ne '..\..\assets\OpenGameMate.AppIcon.ico') {
    throw 'OpenGameMate.App must use the release application icon.'
}

$powerShellScripts = @(
    (Join-Path $repositoryRoot 'scripts\Publish-Portable.ps1'),
    (Join-Path $repositoryRoot 'scripts\Build-Installer.ps1'),
    (Join-Path $repositoryRoot 'scripts\Validate-Privacy.ps1')
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

    if ([System.IO.Path]::GetFileName($scriptPath) -ne 'Validate-Privacy.ps1' -and
        $scriptText.IndexOf('Validate-Privacy.ps1', [StringComparison]::Ordinal) -lt 0) {
        throw "Release script must run the privacy validator before reporting success: $scriptPath"
    }
}

$innoDefinition = Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging\OpenGameMate.iss') -Raw
foreach ($requiredFragment in @(
    '#ifndef MyAppVersion',
    'OutputBaseFilename=OpenGameMate-Setup-{#MyAppVersion}',
    'OutputDir={#MyInstallerOutputDirectory}',
    '#define MyAppIconName "OpenGameMate.AppIcon.2.ico"',
    'DestName: "{#MyAppIconName}"',
    'IconFilename: "{app}\{#MyAppIconName}"',
    'Source: "{#MySourceDirectory}\*"',
    'SetupIconFile=..\assets\OpenGameMate.AppIcon.ico',
    'Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"',
    'Name: "english"; MessagesFile: "compiler:Default.isl"',
    'Description: "{cm:CreateDesktopIcon}"',
    'Description: "{cm:LaunchProgram,{#MyAppName}}"',
    'english.GameAccountRiskTitle=Game account risk warning',
    'chinesesimplified.GameAccountRiskTitle=',
    'GameAccountRiskPage := CreateInputOptionPage(',
    'function PrepareToInstall(var NeedsRestart: Boolean): String;',
    'if not GameAccountRiskPage.Values[0] then'
)) {
    if ($innoDefinition.IndexOf($requiredFragment, [StringComparison]::Ordinal) -lt 0) {
        throw "Inno Setup definition is missing required release macro usage: $requiredFragment"
    }
}

foreach ($forbiddenFragment in @(
    "GetPreviousData('GameAccountRiskAccepted'",
    "SetPreviousData(PreviousDataKey, 'GameAccountRiskAccepted'",
    'GameAccountRiskPreviouslyAccepted',
    'function ShouldSkipPage(PageID: Integer): Boolean;'
)) {
    if ($innoDefinition.IndexOf($forbiddenFragment, [StringComparison]::Ordinal) -ge 0) {
        throw "Inno Setup definition must show the account-risk page for every installation: $forbiddenFragment"
    }
}

Write-Host "Release metadata is consistent for OpenGameMate $version."
