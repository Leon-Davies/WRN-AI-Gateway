param()
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"

Write-Host "WRN AI Gateway Phase 1 verification" -ForegroundColor Cyan

& (Join-Path $PSScriptRoot "build.ps1")

$required = @(
    (Join-Path $dist "WRN-AI-Gateway.exe"),
    (Join-Path $dist "ui\MainWindow.xaml"),
    (Join-Path $root "src\WRN.AIGateway\assets\wrn-ai-gateway.ico")
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

$brandDir = Join-Path $root "src\WRN.AIGateway\local-assets"
if (Test-Path $brandDir) {
    $brandFiles = @(Get-ChildItem $brandDir -File -ErrorAction SilentlyContinue)
    Push-Location $root
    try {
        foreach ($brandFile in $brandFiles) {
            $relative = "src/WRN.AIGateway/local-assets/" + $brandFile.Name
            & git check-ignore --quiet $relative
            if ($LASTEXITCODE -ne 0) {
                throw "Local WRN branding asset is not ignored by git: $relative"
            }
        }
    }
    finally {
        Pop-Location
    }
}

$xaml = Get-Content (Join-Path $src "WRN.AIGateway\ui\MainWindow.xaml") -Raw
$visiblePlaceholders = @(
    "Phase 1 preview",
    "Prototype healthy",
    "Mock connection",
    "Preview catalogue",
    "representative UI data"
)
foreach ($placeholder in $visiblePlaceholders) {
    if ($xaml.Contains($placeholder)) {
        throw "Visible development placeholder found in UI: $placeholder"
    }
}

$requiredUi = @(
    "ActionCardButtonStyle",
    "HeroImageA",
    "HeroImageB",
    "ModelsShortcutButton",
    "UpdatesShortcutButton",
    "ModelDeepSeekButton",
    "ModelSonnetButton",
    "ModelAstraButton",
    "ModelSolButton",
    "ModelLunaButton",
    "ModelGlmButton",
    "ModelRequestButton",
    "ReportBugButton",
    "Brought to you by the Willis Research Network",
    'Height="156"',
    'Tag="wtw"',
    'Tag="wrn"',
    'Padding="20,6,20,12"',
    'Margin="20,0,20,0"'
)
foreach ($required in $requiredUi) {
    if (-not $xaml.Contains($required)) {
        throw "Required owner-polish UI element is missing: $required"
    }
}

if ($xaml.Contains("¢")) {
    throw "Model pricing must be displayed in USD, not cents."
}

$controller = Get-Content (Join-Path $src "WRN.AIGateway\AppController.cs") -Raw
foreach ($requiredControllerText in @(
    "OpenOutlookDraft",
    "CreateDialogShell",
    "WindowStyle = WindowStyle.None",
    "AllowsTransparency = true",
    "CreateFlatButtonTemplate",
    "mailto:",
    "Leon.Davies@wtwco.com",
    '$0.0018',
    '$0.014',
    '$0.070',
    '$0.028',
    '$0.0016',
    '$0.0008'
)) {
    if (-not $controller.Contains($requiredControllerText)) {
        throw "Required controller behaviour is missing: $requiredControllerText"
    }
}

Write-Host "PASS: native app builds" -ForegroundColor Green
Write-Host "PASS: visible development placeholders removed" -ForegroundColor Green
Write-Host "PASS: clickable launch/model/support tiles and rotating hero layers present" -ForegroundColor Green
Write-Host "PASS: no Claude package/history manipulation code in Phase 1 source" -ForegroundColor Green
Write-Host "PASS: local corporate brand asset is excluded from git" -ForegroundColor Green
Write-Host "PASS: Phase 1 remains standard-user only" -ForegroundColor Green
