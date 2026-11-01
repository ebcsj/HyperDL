# Purpose: Validate and package the HyperDL Manifest V3 Chrome extension.
# Requirements: Windows PowerShell 5.1+ on Windows (System.Drawing) and
#               Compress-Archive (Microsoft.PowerShell.Archive).
# Input: extension\ (source files; PNG icons are regenerated on each run).
# Output: artifacts\extension\HyperDL-Chrome.zip.
# The script does not modify C# sources, the installer, or the solution.
$ErrorActionPreference = "Stop"

try {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}
catch {
    throw "System.Drawing is required to generate the extension icons: $($_.Exception.Message)"
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$extensionDir = Join-Path $root "extension"
$iconDir = Join-Path $extensionDir "icons"
$artifactDir = Join-Path $root "artifacts\extension"
$zipPath = Join-Path $artifactDir "HyperDL-Chrome.zip"

$requiredFiles = @(
    "manifest.json",
    "background.js",
    "popup.html",
    "popup.js",
    "options.html",
    "options.js",
    "styles.css",
    "README.md",
    "PRIVACY.md",
    "icons/icon16.png",
    "icons/icon48.png",
    "icons/icon128.png"
)

$allowedPermissions = @("activeTab", "contextMenus", "storage")
$forbiddenManifestProperties = @(
    "host_permissions",
    "optional_host_permissions",
    "content_scripts",
    "web_accessible_resources",
    "externally_connectable"
)
$forbiddenJavaScriptPatterns = @(
    '(?<![\w$.])fetch\s*\(',
    '\bXMLHttpRequest\b',
    '\bWebSocket\b',
    '\bsendBeacon\b',
    '\bimportScripts\s*\(',
    '\bimport\s*\(',
    '\beval\s*\(',
    '\bnew\s+Function\s*\(',
    '\bchrome\.scripting\b',
    '\bchrome\.tabs\.executeScript\b',
    '\bchrome\.tabs\.insertCSS\b'
)

function Get-ExtensionPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    return Join-Path $extensionDir ($RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
}

function New-RoundedRectanglePath {
    param(
        [Parameter(Mandatory = $true)][single]$X,
        [Parameter(Mandatory = $true)][single]$Y,
        [Parameter(Mandatory = $true)][single]$Width,
        [Parameter(Mandatory = $true)][single]$Height,
        [Parameter(Mandatory = $true)][single]$Radius
    )

    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $diameter, $diameter, [single]180, [single]90)
    $path.AddArc(($X + $Width - $diameter), $Y, $diameter, $diameter, [single]270, [single]90)
    $path.AddArc(($X + $Width - $diameter), ($Y + $Height - $diameter), $diameter, $diameter, [single]0, [single]90)
    $path.AddArc($X, ($Y + $Height - $diameter), $diameter, $diameter, [single]90, [single]90)
    $path.CloseFigure()
    return $path
}

function New-ExtensionIcon {
    param(
        [Parameter(Mandatory = $true)][int]$Size,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    # Draw at 4x and downsample for clean anti-aliased edges at 16 px.
    $supersample = 4
    $canvas = $Size * $supersample

    $bitmap = $null
    $graphics = $null
    $backgroundPath = $null
    $gradient = $null
    $borderPen = $null
    $font = $null
    $textFormat = $null
    $scaled = $null
    $scaledGraphics = $null

    try {
        $bitmap = New-Object System.Drawing.Bitmap($canvas, $canvas, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $backgroundPath = New-RoundedRectanglePath -X 0 -Y 0 -Width $canvas -Height $canvas -Radius ($canvas * 0.22)
        $rect = New-Object System.Drawing.RectangleF(0, 0, [single]$canvas, [single]$canvas)
        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 10, 28, 62), [System.Drawing.Color]::FromArgb(255, 28, 80, 170), [single]90)
        $graphics.FillPath($gradient, $backgroundPath)

        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(52, 255, 255, 255), [single]($canvas * 0.008))
        $graphics.DrawPath($borderPen, $backgroundPath)

        $font = New-Object System.Drawing.Font("Segoe UI", [single]($canvas * 0.46), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $textFormat = New-Object System.Drawing.StringFormat
        $textFormat.Alignment = [System.Drawing.StringAlignment]::Center
        $textFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
        $textRect = New-Object System.Drawing.RectangleF([single]0, [single]($canvas * 0.03), [single]$canvas, [single]($canvas * 0.58))
        $graphics.DrawString("H", $font, [System.Drawing.Brushes]::White, $textRect, $textFormat)

        $whiteBrush = [System.Drawing.Brushes]::White
        $shaftWidth = [single]($canvas * 0.095)
        $shaftX = [single](($canvas - $shaftWidth) / 2)
        $shaftY = [single]($canvas * 0.59)
        $shaftHeight = [single]($canvas * 0.19)
        $graphics.FillRectangle($whiteBrush, $shaftX, $shaftY, $shaftWidth, $shaftHeight)

        $arrowPoints = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($canvas * 0.26), [single]($canvas * 0.745))),
            (New-Object System.Drawing.PointF([single]($canvas * 0.74), [single]($canvas * 0.745))),
            (New-Object System.Drawing.PointF([single]($canvas * 0.50), [single]($canvas * 0.94)))
        )
        $graphics.FillPolygon($whiteBrush, $arrowPoints)

        $scaled = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $scaledGraphics = [System.Drawing.Graphics]::FromImage($scaled)
        $scaledGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaledGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scaledGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $destination = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
        $scaledGraphics.DrawImage($bitmap, $destination)
        $scaled.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        foreach ($resource in @($scaledGraphics, $scaled, $textFormat, $font, $borderPen, $gradient, $backgroundPath, $graphics, $bitmap)) {
            if ($null -ne $resource) {
                $resource.Dispose()
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $extensionDir -PathType Container)) {
    throw "Extension folder not found: $extensionDir"
}

New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

Write-Output "Generating PNG icons with System.Drawing..."
foreach ($size in @(16, 48, 128)) {
    $iconPath = Get-ExtensionPath "icons/icon$size.png"
    New-ExtensionIcon -Size $size -OutputPath $iconPath
    Write-Output "  icons/icon$size.png"
}

Write-Output "Validating JSON files..."
$jsonFiles = @(Get-ChildItem -LiteralPath $extensionDir -Filter "*.json" -File -Recurse)
if ($jsonFiles.Count -eq 0) {
    throw "No JSON files found in $extensionDir"
}
foreach ($jsonFile in $jsonFiles) {
    try {
        $null = Get-Content -LiteralPath $jsonFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Invalid JSON in $($jsonFile.FullName): $($_.Exception.Message)"
    }
    Write-Output "  OK: $($jsonFile.FullName)"
}

Write-Output "Validating required files..."
foreach ($relativePath in $requiredFiles) {
    $fullPath = Get-ExtensionPath $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required file is missing: $relativePath"
    }
    if ((Get-Item -LiteralPath $fullPath).Length -le 0) {
        throw "Required file is empty: $relativePath"
    }
}
Write-Output "  $($requiredFiles.Count) required files present and non-empty."

$manifestPath = Get-ExtensionPath "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ($manifest.manifest_version -ne 3) {
    throw "manifest.json must declare manifest_version 3."
}
if ([string]::IsNullOrWhiteSpace([string]$manifest.name)) {
    throw "manifest.json must declare a name."
}
if ([string]$manifest.version -notmatch '^[0-9]+(\.[0-9]+){1,3}$') {
    throw "manifest.json has an invalid version: $($manifest.version)"
}
if ([string]::IsNullOrWhiteSpace([string]$manifest.description)) {
    throw "manifest.json must declare a description."
}

Write-Output "Validating permissions and security constraints..."
$permissions = @($manifest.permissions)
foreach ($permission in $permissions) {
    if ($allowedPermissions -notcontains [string]$permission) {
        throw "Unexpected permission in manifest.json: $permission"
    }
}
foreach ($permission in $allowedPermissions) {
    if ($permissions -notcontains $permission) {
        throw "Required permission is missing from manifest.json: $permission"
    }
}
foreach ($propertyName in $forbiddenManifestProperties) {
    if ($manifest.PSObject.Properties.Name -contains $propertyName) {
        throw "manifest.json must not declare $propertyName."
    }
}

if (-not $manifest.content_security_policy -or
    [string]::IsNullOrWhiteSpace([string]$manifest.content_security_policy.extension_pages)) {
    throw "manifest.json must declare content_security_policy.extension_pages."
}
$contentSecurityPolicy = [string]$manifest.content_security_policy.extension_pages
if ($contentSecurityPolicy -notmatch "script-src\s+'self'") {
    throw "The extension CSP must restrict script-src to 'self'."
}
if ($contentSecurityPolicy -notmatch "object-src\s+'self'") {
    throw "The extension CSP must restrict object-src to 'self'."
}
if ($contentSecurityPolicy -match "unsafe-eval|unsafe-inline|https?:|data:") {
    throw "The extension CSP contains a forbidden source expression: $contentSecurityPolicy"
}

$manifestReferences = New-Object System.Collections.Generic.List[string]
function Add-ManifestReference {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Location
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "Manifest $Location is empty."
    }

    $normalized = $RelativePath -replace '\\', '/'
    if ($normalized -match '^[a-z][a-z0-9+.-]*:' -or $normalized.StartsWith('//')) {
        throw "Manifest $Location must reference a local file, not $RelativePath."
    }

    $fullPath = Get-ExtensionPath $normalized
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Manifest $Location references a missing file: $RelativePath"
    }
    $manifestReferences.Add($normalized)
}

if (-not $manifest.background -or [string]::IsNullOrWhiteSpace([string]$manifest.background.service_worker)) {
    throw "manifest.json must declare background.service_worker."
}
Add-ManifestReference ([string]$manifest.background.service_worker) "background.service_worker"

if (-not $manifest.action -or [string]::IsNullOrWhiteSpace([string]$manifest.action.default_popup)) {
    throw "manifest.json must declare action.default_popup."
}
Add-ManifestReference ([string]$manifest.action.default_popup) "action.default_popup"

if (-not $manifest.options_ui -or [string]::IsNullOrWhiteSpace([string]$manifest.options_ui.page)) {
    throw "manifest.json must declare options_ui.page."
}
Add-ManifestReference ([string]$manifest.options_ui.page) "options_ui.page"

if (-not $manifest.icons -or -not $manifest.action.default_icon) {
    throw "manifest.json must declare icons and action.default_icon."
}
foreach ($size in @("16", "48", "128")) {
    if ($manifest.icons.PSObject.Properties.Name -notcontains $size) {
        throw "manifest.json icons are missing size $size."
    }
    if ($manifest.action.default_icon.PSObject.Properties.Name -notcontains $size) {
        throw "manifest.json action.default_icon is missing size $size."
    }
}
foreach ($property in $manifest.icons.PSObject.Properties) {
    Add-ManifestReference ([string]$property.Value) "icons.$($property.Name)"
}
foreach ($property in $manifest.action.default_icon.PSObject.Properties) {
    Add-ManifestReference ([string]$property.Value) "action.default_icon.$($property.Name)"
}
Write-Output "  Manifest references: $($manifestReferences.Count) local files."

Write-Output "Validating PNG icons..."
foreach ($size in @(16, 48, 128)) {
    $iconPath = Get-ExtensionPath "icons/icon$size.png"
    $image = [System.Drawing.Image]::FromFile($iconPath)
    try {
        if ($image.Width -ne $size -or $image.Height -ne $size) {
            throw "icons/icon$size.png has the wrong dimensions: $($image.Width)x$($image.Height)"
        }
        if ($image.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) {
            throw "icons/icon$size.png is not a PNG file."
        }
    }
    finally {
        $image.Dispose()
    }
    Write-Output "  OK: icons/icon$size.png ($size x $size)"
}

Write-Output "Validating HTML references and inline code..."
$htmlFiles = @(Get-ChildItem -LiteralPath $extensionDir -Filter "*.html" -File)
foreach ($htmlFile in $htmlFiles) {
    $html = Get-Content -LiteralPath $htmlFile.FullName -Raw -Encoding UTF8
    if ($html -match '(?s)<script\b(?![^>]*\bsrc\s*=)[^>]*>') {
        throw "Inline <script> found in $($htmlFile.Name); use an external file."
    }
    if ($html -match '\son[a-z]+\s*=') {
        throw "Inline event handler found in $($htmlFile.Name)."
    }

    $referencePattern = '(?:src|href)\s*=\s*["'']([^"'']+)["'']'
    $references = [regex]::Matches($html, $referencePattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    foreach ($match in $references) {
        $reference = $match.Groups[1].Value.Trim()
        if ($reference.StartsWith("#")) {
            continue
        }
        if ($reference -match '^[a-z][a-z0-9+.-]*:' -or $reference.StartsWith('//')) {
            throw "External reference found in $($htmlFile.Name): $reference"
        }
        $cleanReference = ($reference -split '[?#]')[0]
        if ([string]::IsNullOrWhiteSpace($cleanReference)) {
            continue
        }
        $fullPath = Get-ExtensionPath $cleanReference
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "$($htmlFile.Name) references a missing file: $reference"
        }
    }
}
Write-Output "  HTML references are local and exist."

Write-Output "Validating JavaScript restrictions..."
$javascriptFiles = @(Get-ChildItem -LiteralPath $extensionDir -Filter "*.js" -File)
foreach ($javascriptFile in $javascriptFiles) {
    $javascript = Get-Content -LiteralPath $javascriptFile.FullName -Raw -Encoding UTF8
    foreach ($pattern in $forbiddenJavaScriptPatterns) {
        if ($javascript -match $pattern) {
            throw "Forbidden API pattern '$pattern' found in $($javascriptFile.Name)."
        }
    }
}
Write-Output "  No network, eval, content-script, or remote-code APIs found."

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Output "Creating ZIP package..."
$tempZipPath = Join-Path $artifactDir "HyperDL-Chrome.tmp.zip"
if (Test-Path -LiteralPath $tempZipPath) {
    Remove-Item -LiteralPath $tempZipPath -Force
}

try {
    Compress-Archive -Path (Join-Path $extensionDir "*") -DestinationPath $tempZipPath -CompressionLevel Optimal

    # Windows PowerShell 5.1 writes backslash separators into the archive.
    # The ZIP specification and Chrome expect forward slashes, so repack the
    # entries with normalized names.
    $zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    $destinationArchive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
    $sourceArchive = [System.IO.Compression.ZipFile]::OpenRead($tempZipPath)
    try {
        foreach ($entry in $sourceArchive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $normalizedEntryName = $entry.FullName -replace '\\', '/'
            $newEntry = $destinationArchive.CreateEntry($normalizedEntryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $sourceStream = $entry.Open()
            $destinationStream = $newEntry.Open()
            try {
                $sourceStream.CopyTo($destinationStream)
            }
            finally {
                $destinationStream.Dispose()
                $sourceStream.Dispose()
            }
        }
    }
    finally {
        $sourceArchive.Dispose()
        $destinationArchive.Dispose()
        $zipStream.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $tempZipPath) {
        Remove-Item -LiteralPath $tempZipPath -Force
    }
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    if ($entryNames -notcontains "manifest.json") {
        throw "The ZIP package does not contain manifest.json at its root."
    }
    if ($entryNames | Where-Object { $_ -match '\\' }) {
        throw "The ZIP package contains non-standard backslash entry names."
    }
    foreach ($relativePath in $requiredFiles) {
        $zipEntryName = $relativePath -replace '\\', '/'
        if ($entryNames -notcontains $zipEntryName) {
            throw "The ZIP package is missing: $zipEntryName"
        }
    }
    foreach ($relativePath in $manifestReferences) {
        $zipEntryName = $relativePath -replace '\\', '/'
        if ($entryNames -notcontains $zipEntryName) {
            throw "The ZIP package is missing a manifest reference: $zipEntryName"
        }
    }
}
finally {
    $archive.Dispose()
}

$zipFile = Get-Item -LiteralPath $zipPath
if ($zipFile.Length -le 0) {
    throw "The ZIP package is empty: $zipPath"
}
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256

Write-Output ""
Write-Output "Extension folder: $extensionDir"
Write-Output "Manifest version: $($manifest.version)"
Write-Output "ZIP path: $($zipFile.FullName)"
Write-Output "ZIP size: $($zipFile.Length) bytes"
Write-Output "ZIP entries: $($entryNames.Count)"
Write-Output "SHA-256: $($hash.Hash)"
Write-Output "Validation: passed"
