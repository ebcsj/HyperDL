# Purpose: Restore, build, test, and publish the Windows x64 application.
# Requirements: Windows x64 and the .NET 8 SDK at the configured dotnet path.
# Input: HyperDL.sln, HyperDL.csproj, and THIRD_PARTY_NOTICES.md.
# Output: artifacts\publish\HyperDL\; no files are explicitly deleted.
$ErrorActionPreference = "Stop"

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $root "HyperDL.sln"
$project = Join-Path $root "HyperDL.csproj"
$publishOutput = Join-Path $root "artifacts\publish\HyperDL"
$executable = Join-Path $publishOutput "HyperDL.exe"

if (-not (Test-Path -LiteralPath $solution -PathType Leaf) -or
    -not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Run this script from a valid HyperDL source checkout."
}

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "x64 .NET SDK was not found at $dotnet"
}

& $dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw "HyperDL restore failed with exit code $LASTEXITCODE"
}

& $dotnet build $solution -c Release
if ($LASTEXITCODE -ne 0) {
    throw "HyperDL Release build failed with exit code $LASTEXITCODE"
}

& $dotnet test $solution -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "HyperDL Release tests failed with exit code $LASTEXITCODE"
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishTrimmed=false `
    -o $publishOutput
if ($LASTEXITCODE -ne 0) {
    throw "HyperDL win-x64 publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published HyperDL.exe was not found at $executable"
}

Copy-Item -LiteralPath (Join-Path $root "THIRD_PARTY_NOTICES.md") `
    -Destination $publishOutput -Force

Write-Output "Published HyperDL.exe: $executable"
Write-Output "Publish directory: $publishOutput"
