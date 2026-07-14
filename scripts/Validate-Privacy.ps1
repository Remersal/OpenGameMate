[CmdletBinding()]
param(
    [string[]]$ArtifactPath = @()
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localUserPathPattern = [regex]::new(
    '(?i)(?:\\\\\?\\)?[A-Z]:[\\/]+Users[\\/]+[^\\/\x00\r\n]+',
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
$textExtensions = @(
    '', '.cs', '.csproj', '.gitignore', '.iss', '.json', '.md', '.props', '.ps1',
    '.targets', '.txt', '.xaml', '.xml', '.yaml', '.yml'
)
$artifactExtensions = @(
    '.config', '.dll', '.exe', '.json', '.md', '.pdb', '.ps1', '.txt', '.xml'
)
$violations = [System.Collections.Generic.List[string]]::new()

function Test-TextFile {
    param([string]$Path)

    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    return $textExtensions -contains $extension
}

function Add-TextViolations {
    param(
        [string]$Path,
        [string]$DisplayPath
    )

    if (-not (Test-TextFile -Path $Path)) {
        return
    }

    $content = [System.IO.File]::ReadAllText($Path)
    if ($localUserPathPattern.IsMatch($content)) {
        $violations.Add("tracked-text:$DisplayPath")
    }
}

function Add-BinaryViolations {
    param(
        [string]$Path,
        [string]$DisplayPath
    )

    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($artifactExtensions -notcontains $extension) {
        return
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
    if ($localUserPathPattern.IsMatch($ascii) -or
        $localUserPathPattern.IsMatch($utf16)) {
        $violations.Add("release-artifact:$DisplayPath")
    }
}

$trackedFiles = & git -C $repositoryRoot ls-files
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE"
}

foreach ($relativePath in $trackedFiles) {
    $fullPath = Join-Path $repositoryRoot $relativePath
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        Add-TextViolations -Path $fullPath -DisplayPath $relativePath
    }
}

foreach ($candidate in $ArtifactPath) {
    $resolved = [System.IO.Path]::GetFullPath($candidate)
    if (Test-Path -LiteralPath $resolved -PathType Leaf) {
        Add-BinaryViolations -Path $resolved -DisplayPath $resolved
        continue
    }

    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "Privacy validation path does not exist: $resolved"
    }

    foreach ($file in Get-ChildItem -LiteralPath $resolved -Recurse -File) {
        Add-BinaryViolations -Path $file.FullName -DisplayPath $file.FullName
    }
}

if ($violations.Count -gt 0) {
    $detail = $violations | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Privacy validation found local user paths:`n$($detail -join "`n")"
}

Write-Host 'Privacy validation passed: no local Windows user paths were found.'
