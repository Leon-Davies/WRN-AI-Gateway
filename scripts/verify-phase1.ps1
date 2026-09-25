param()
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"

Write-Host "WRN AI Gateway Phase 1 verification" -ForegroundColor Cyan

& (Join-Path $PSScriptRoot "build.ps1")

$required = @(
    (Join-Path $dist "WRN-AI-Gateway.exe"),
    (Join-Path $dist "ui\MainWindow.xaml")
)
foreach ($path in $required) {
    if (-not (Test-Path $path)) { throw "Missing build output: $path" }
}

$forbidden = @(
    "Remove-AppxPackage",
    "Add-AppxPackage",
    "Get-AppxPackage",
    "Program Files\WindowsApps",
    "Claude-3p",
    "requireAdministrator",
    "runas"
)

$sourceFiles = Get-ChildItem $src -Recurse -File | Where-Object {
    $_.Extension -in @(".cs", ".xaml", ".ps1", ".cmd")
}
foreach ($pattern in $forbidden) {
    $hit = $sourceFiles | Select-String -SimpleMatch $pattern -ErrorAction SilentlyContinue
    if ($hit) {
        throw "Forbidden Phase 1 implementation dependency found: $pattern"
    }
}

$brandPath = Join-Path $root "src\WRN.AIGateway\local-assets\wrn-hero.png"
if (Test-Path $brandPath) {
    Push-Location $root
    try {
        & git check-ignore --quiet "src/WRN.AIGateway/local-assets/wrn-hero.png"
        if ($LASTEXITCODE -ne 0) {
            throw "Local WRN branding asset is not ignored by git."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "PASS: native app builds" -ForegroundColor Green
Write-Host "PASS: no Claude package/history manipulation code in Phase 1 source" -ForegroundColor Green
Write-Host "PASS: local corporate brand asset is excluded from git" -ForegroundColor Green
Write-Host "PASS: Phase 1 remains standard-user only" -ForegroundColor Green
