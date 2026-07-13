[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$version = '150.0.4078.65'
$downloadUrl = 'https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/c00b9782-0422-4114-be27-8eec079b394d/Microsoft.WebView2.FixedVersionRuntime.150.0.4078.65.x64.cab'
$phase0Root = Join-Path $env:LOCALAPPDATA 'OpenGameMate\Phase0'
$runtimeRoot = Join-Path $phase0Root 'WebView2Runtime'
$runtimeFolder = Join-Path $runtimeRoot "Microsoft.WebView2.FixedVersionRuntime.$version.x64"
$runtimeExecutable = Join-Path $runtimeFolder 'msedgewebview2.exe'
$downloadFolder = Join-Path $env:TEMP 'OpenGameMate'
$cabPath = Join-Path $downloadFolder "Microsoft.WebView2.FixedVersionRuntime.$version.x64.cab"

if (Test-Path -LiteralPath $runtimeExecutable) {
    $installedSignature = Get-AuthenticodeSignature -LiteralPath $runtimeExecutable
    if ($installedSignature.Status -ne 'Valid' -or
        $installedSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "The existing fixed runtime signature is invalid. No files were deleted or overwritten: $runtimeExecutable"
    }

    Write-Host "The isolated WebView2 Runtime is already installed: $version"
    exit 0
}

if (Test-Path -LiteralPath $runtimeRoot) {
    throw "The runtime root exists but the requested version is incomplete. Inspect it manually; this script will not bulk-delete or overwrite it: $runtimeRoot"
}

New-Item -ItemType Directory -Path $downloadFolder -Force | Out-Null

if (-not (Test-Path -LiteralPath $cabPath)) {
    & curl.exe -L --fail --silent --show-error $downloadUrl -o $cabPath
    if ($LASTEXITCODE -ne 0) {
        throw "WebView2 Fixed Runtime download failed. curl exit code: $LASTEXITCODE"
    }
}

$cabSignature = Get-AuthenticodeSignature -LiteralPath $cabPath
if ($cabSignature.Status -ne 'Valid' -or
    $cabSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw "The downloaded CAB failed Microsoft Authenticode verification: $cabPath"
}

New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
& expand.exe $cabPath -F:* $runtimeRoot *> $null
if ($LASTEXITCODE -ne 0) {
    throw "WebView2 Fixed Runtime extraction failed. expand exit code: $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $runtimeExecutable)) {
    throw "The WebView2 executable was not found after extraction: $runtimeExecutable"
}

$runtimeSignature = Get-AuthenticodeSignature -LiteralPath $runtimeExecutable
if ($runtimeSignature.Status -ne 'Valid' -or
    $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw "The extracted WebView2 executable failed Microsoft Authenticode verification."
}

Write-Host "Installed isolated WebView2 Fixed Runtime: $version"
Write-Host "Location: $runtimeFolder"
