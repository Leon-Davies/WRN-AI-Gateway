param(
    [string]$Version = "0.1.0-dev",
    [int]$AppRelease = 0,
    [string]$BrandAssetDirectory = "",
    [switch]$RequireBranding
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$releaseRoot = Join-Path $root "release"
$release = Join-Path $releaseRoot ("WRN-AI-Gateway-v" + $Version)
$appDir = Join-Path $release "app"

if ([string]::IsNullOrWhiteSpace($BrandAssetDirectory)) {
    & (Join-Path $PSScriptRoot "build.ps1")
} else {
    & (Join-Path $PSScriptRoot "build.ps1") -BrandAssetDirectory $BrandAssetDirectory
}

if ($RequireBranding) {
    $heroes = @(Get-ChildItem (Join-Path $dist "assets") -Filter "wrn-hero*.png" -File -ErrorAction SilentlyContinue)
    if ($heroes.Count -eq 0) {
        throw "Required WRN hero images were not included in the build."
    }
}

if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Copy-Item (Join-Path $dist "*") $appDir -Recurse -Force

$identity = [ordered]@{
    schemaVersion = 1
    release = $AppRelease
    version = $Version
}
$identityJson = $identity | ConvertTo-Json
[IO.File]::WriteAllText(
    (Join-Path $appDir "app-release.json"),
    ($identityJson + [Environment]::NewLine),
    (New-Object Text.UTF8Encoding($false)))

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$setupSource = Join-Path $root "src\WRN.AIGateway.Setup\Setup.cs"
$setupExe = Join-Path $release "WRN-AI-Gateway-Setup.exe"
$payloadZip = Join-Path $releaseRoot ("WRN-AI-Gateway-v" + $Version + "-payload.zip")
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $payloadZip -CompressionLevel Optimal
$icon = Join-Path $root "src\WRN.AIGateway\assets\wrn-ai-gateway.ico"

$compileArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$setupExe",
    "/reference:$framework\System.dll",
    "/reference:$framework\System.Core.dll",
    "/reference:$framework\Microsoft.CSharp.dll",
    "/reference:$framework\System.Drawing.dll",
    "/reference:$framework\System.Windows.Forms.dll",
    "/reference:$framework\System.IO.Compression.dll",
    "/reference:$framework\System.IO.Compression.FileSystem.dll",
    "/resource:$payloadZip,WRN.AIGateway.Payload.zip"
)
if (Test-Path $icon) {
    $compileArgs += "/win32icon:$icon"
}
$compileArgs += $setupSource
& $csc $compileArgs
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed with exit code $LASTEXITCODE" }
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

$readme = @(
    "WRN AI Gateway",
    "",
    "1. Double-click WRN-AI-Gateway-Setup.exe",
    "2. Click Install",
    "3. Open WRN AI Gateway."
)
$readme | Set-Content (Join-Path $release "README-FIRST.txt") -Encoding UTF8
$Version | Set-Content (Join-Path $release "VERSION.txt") -Encoding ASCII

$hashTargets = @(
    (Join-Path $release "WRN-AI-Gateway-Setup.exe"),
    (Join-Path $appDir "WRN-AI-Gateway.exe"),
    (Join-Path $appDir "WRN-AI-Gateway-Gateway.exe"),
    (Join-Path $appDir "WRN-AI-Gateway-Updater.exe"),
    (Join-Path $appDir "app-release.json"),
    (Join-Path $appDir "ui\MainWindow.xaml"),
    (Join-Path $appDir "catalogue\catalogue.json"),
    (Join-Path $appDir "catalogue\catalogue.sig")
)
$hashTargets += @(
    Get-ChildItem (Join-Path $appDir "assets") -Filter "wrn-hero*.png" -File -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName
)
$hashLines = foreach ($target in $hashTargets) {
    $hash = Get-FileHash $target -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), ($target.Substring($release.Length + 1))
}
$hashLines | Set-Content (Join-Path $release "SHA256SUMS.txt") -Encoding ASCII

$zip = $release + ".zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $release "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Release package ready:" -ForegroundColor Green
Write-Host "  $release"
Write-Host "  $zip"
