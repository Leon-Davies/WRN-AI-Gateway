param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
$uiOut = Join-Path $dist "ui"
$assetOut = Join-Path $dist "assets"
$catalogueOut = Join-Path $dist "catalogue"

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = Join-Path $framework "WPF"

if (-not (Test-Path $csc)) {
    throw "The built-in .NET Framework compiler was not found."
}

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $dist | Out-Null
New-Item -ItemType Directory -Path $uiOut | Out-Null
New-Item -ItemType Directory -Path $assetOut | Out-Null
New-Item -ItemType Directory -Path $catalogueOut | Out-Null

Copy-Item (Join-Path $src "ui\MainWindow.xaml") (Join-Path $uiOut "MainWindow.xaml")
Copy-Item (Join-Path $src "catalogue\catalogue.json") (Join-Path $catalogueOut "catalogue.json")
Copy-Item (Join-Path $src "catalogue\catalogue.sig") (Join-Path $catalogueOut "catalogue.sig")

$localAssetDir = Join-Path $src "local-assets"
$localHeroes = @()
if (Test-Path $localAssetDir) {
    $localHeroes = @(Get-ChildItem $localAssetDir -Filter "wrn-hero*.png" -File | Sort-Object Name)
}

if ($localHeroes.Count -gt 0) {
    foreach ($hero in $localHeroes) {
        Copy-Item $hero.FullName (Join-Path $assetOut $hero.Name)
    }
    Write-Host ("Using {0} local WRN branding assets (not tracked by git)." -f $localHeroes.Count) -ForegroundColor DarkMagenta
} else {
    Write-Host "No local brand images found; the built-in gradient fallback will be used." -ForegroundColor DarkYellow
}

$references = @(
    (Join-Path $framework "System.dll"),
    (Join-Path $framework "System.Core.dll"),
    (Join-Path $framework "System.Xaml.dll"),
    (Join-Path $framework "System.DirectoryServices.dll"),
    (Join-Path $framework "System.DirectoryServices.AccountManagement.dll"),
    (Join-Path $framework "System.Web.Extensions.dll"),
    (Join-Path $wpf "WindowsBase.dll"),
    (Join-Path $wpf "PresentationCore.dll"),
    (Join-Path $wpf "PresentationFramework.dll")
)

$icon = Join-Path $src "assets\wrn-ai-gateway.ico"

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$dist\WRN-AI-Gateway.exe"
)

if (Test-Path $icon) {
    $args += "/win32icon:$icon"
}

foreach ($reference in $references) {
    if (-not (Test-Path $reference)) {
        throw "Required framework assembly was not found: $reference"
    }
    $args += "/reference:$reference"
}

$args += (Join-Path $src "Program.cs")
$args += (Join-Path $src "AppController.cs")
$args += (Join-Path $src "ClaudeDiscovery.cs")
$args += (Join-Path $src "ModelCatalogue.cs")
$args += (Join-Path $src "ClaudeTransition.cs")
$args += (Join-Path $src "ModeCoordinator.cs")
$args += (Join-Path $src "GatewayLifecycle.cs")

& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

& (Join-Path $PSScriptRoot "build-gateway.ps1") | Out-Null

Write-Host ""
Write-Host "Build complete:" -ForegroundColor Green
Write-Host "  $dist\WRN-AI-Gateway.exe"
Write-Host ""
Write-Host "Phase 2 discovery is read-only with respect to Claude configuration; live switching is not enabled." -ForegroundColor Cyan

if ($Run) {
    Start-Process (Join-Path $dist "WRN-AI-Gateway.exe")
}
