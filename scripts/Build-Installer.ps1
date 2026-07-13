[CmdletBinding()]
param(
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

& (Join-Path $PSScriptRoot 'Publish-Portable.ps1')

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

& $InnoCompiler (Join-Path $repositoryRoot 'packaging\OpenGameMate.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}
