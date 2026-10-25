# Purpose: Publish HyperDL and compile the per-user Inno Setup installer.
# Requirements: Windows x64, .NET 8 SDK, and Inno Setup 6.
# Input: HyperDL source plus installer\HyperDL.iss.
# Output: artifacts\publish\HyperDL\ and installer\Output\HyperDL_Setup.exe;
#         no files are explicitly deleted.
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $root "HyperDL.sln"
$installerScript = Join-Path $root "installer\HyperDL.iss"
$expectedOutput = Join-Path $root "installer\Output\HyperDL_Setup.exe"
$compilerCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
)

if (-not (Test-Path -LiteralPath $solution -PathType Leaf) -or
    -not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "Run this script from a valid HyperDL source checkout."
}

& (Join-Path $PSScriptRoot "publish.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "HyperDL publishing failed with exit code $LASTEXITCODE"
}

$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1

if (-not $compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $compiler = $command.Source
    }
}

if (-not $compiler) {
    [Console]::Error.WriteLine("Inno Setup 6 is required to build HyperDL_Setup.exe.")
    exit 1
}

& $compiler $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $expectedOutput -PathType Leaf)) {
    throw "Setup executable was not created at $expectedOutput"
}

$setup = Get-Item -LiteralPath $expectedOutput
if ($setup.Length -le 0) {
    throw "Setup executable is empty: $expectedOutput"
}

$hash = Get-FileHash -LiteralPath $expectedOutput -Algorithm SHA256
Write-Output "Setup path: $($setup.FullName)"
Write-Output "File size: $($setup.Length) bytes"
Write-Output "SHA-256: $($hash.Hash)"
