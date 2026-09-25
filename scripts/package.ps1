param([string]$Version = "0.1.0-dev")
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$releaseRoot = Join-Path $root "release"
$release = Join-Path $releaseRoot ("WRN-AI-Gateway-v" + $Version)
$appDir = Join-Path $release "app"

& (Join-Path $PSScriptRoot "build.ps1")

if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Copy-Item (Join-Path $dist "*") $appDir -Recurse -Force

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$setupSource = Join-Path $root "src\WRN.AIGateway.Setup\Setup.cs"
$setupExe = Join-Path $release "WRN-AI-Gateway-Setup.exe"

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
    $setupSource
)
& $csc $compileArgs
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed with exit code $LASTEXITCODE" }

$readme = @(
    "WRN AI Gateway - Phase 1 Preview",
    "",
    "1. Double-click WRN-AI-Gateway-Setup.exe",
    "2. Click Install",
    "3. Open WRN AI Gateway from the desktop or Start menu.",
    "",
    "No administrator rights are required.",
    "This preview does not change Claude configuration."
)
$readme | Set-Content (Join-Path $release "README-FIRST.txt") -Encoding UTF8
$Version | Set-Content (Join-Path $release "VERSION.txt") -Encoding ASCII

$hashTargets = @(
    (Join-Path $release "WRN-AI-Gateway-Setup.exe"),
    (Join-Path $appDir "WRN-AI-Gateway.exe"),
    (Join-Path $appDir "ui\MainWindow.xaml")
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
