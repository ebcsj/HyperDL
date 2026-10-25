# Purpose: Run read-only static checks against the installer script and Setup.
# Requirements: PowerShell and a previously built installer executable.
# Input: installer\HyperDL.iss and installer\Output\HyperDL_Setup.exe.
# Output: Validation results and the Setup SHA-256; no files are modified/deleted.
$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$installerScript = Join-Path $root "installer\HyperDL.iss"
$setupPath = Join-Path $root "installer\Output\HyperDL_Setup.exe"

if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "Inno Setup script is missing: $installerScript"
}

if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "HyperDL_Setup.exe is missing: $setupPath"
}

$setup = Get-Item -LiteralPath $setupPath
if ($setup.Name -cne "HyperDL_Setup.exe" -or $setup.Length -le 0) {
    throw "Installer filename or size is invalid: $setupPath"
}

$hash = Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
if ($hash.Hash.Length -ne 64) {
    throw "Could not calculate a valid SHA-256 checksum for $setupPath"
}

$script = [System.IO.File]::ReadAllText($installerScript)
$requiredPatterns = @(
    '(?m)^#define AppName "HyperDL"\r?$',
    '(?m)^AppName=\{#AppName\}\r?$',
    '(?m)^AppPublisher=\{#AppPublisher\}\r?$',
    '(?m)^Uninstallable=yes\r?$',
    '(?m)^UninstallDisplayName=HyperDL\r?$',
    '(?m)^UninstallDisplayIcon=\{app\}\\HyperDL\.exe\r?$',
    '(?m)^OutputBaseFilename=HyperDL_Setup\r?$',
    '\{uninstallexe\}',
    'Uninstall HyperDL',
    '\.\.\\artifacts\\publish\\HyperDL\\\*',
    'PrivilegesRequired=lowest',
    'DefaultDirName=\{localappdata\}\\Programs\\HyperDL'
)

foreach ($pattern in $requiredPatterns) {
    if ($script -notmatch $pattern) {
        throw "Inno Setup script is missing required installer configuration: $pattern"
    }
}

$forbiddenPatterns = @(
    'ChromeExtension',
    'NativeHost',
    'NativeMessagingHosts',
    'NativeMessaging',
    'HyperDL\.NativeHost',
    'HyperDL\.ChromeExtension',
    '\[UninstallDelete\]',
    'hyperdl\.db',
    '\.part',
    'Downloads',
    '\{localappdata\}\\HyperDL'
)

foreach ($pattern in $forbiddenPatterns) {
    if ($script -match $pattern) {
        throw "Unsafe or removed integration content found in installer script: $pattern"
    }
}

Write-Output "Installer static validation: PASS"
Write-Output "Setup path: $($setup.FullName)"
Write-Output "File size: $($setup.Length) bytes"
Write-Output "SHA-256: $($hash.Hash)"
