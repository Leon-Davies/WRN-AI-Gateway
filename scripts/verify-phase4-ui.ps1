$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$xamlPath = Join-Path $src "ui\MainWindow.xaml"
$controllerPath = Join-Path $src "AppController.cs"
$credentialPath = Join-Path $src "OpenRouterCredentials.cs"

& (Join-Path $PSScriptRoot "build.ps1")

$xaml = Get-Content -LiteralPath $xamlPath -Raw
$controller = Get-Content -LiteralPath $controllerPath -Raw
$credentials = Get-Content -LiteralPath $credentialPath -Raw

foreach ($requiredUi in @(
    "SettingsNavButton",
    "SettingsPage",
    "CredentialStatusBadge",
    "CredentialStatusText",
    "CredentialConnectButton",
    "CredentialTestButton",
    "CredentialRemoveButton"
)) {
    if (-not $xaml.Contains($requiredUi)) {
        throw "Missing Phase 4 UI element: $requiredUi"
    }
}

foreach ($requiredController in @(
    'RegisterPage("Settings"',
    "new PasswordBox",
    "ValidateAndSave",
    "TestStored",
    "ShowRemoveOpenRouterCredentialDialog",
    'ShowPage("Settings")'
)) {
    if (-not $controller.Contains($requiredController)) {
        throw "Missing Phase 4 controller behavior: $requiredController"
    }
}

if ($controller.Contains("ClaudeTransitionExecutor.Execute")) {
    throw "Credential onboarding must not enable live Claude switching."
}

foreach ($unsafeUiPattern in @(
    "Clipboard.SetText(candidate)",
    "Clipboard.SetText(password.Password)",
    "ShowToast(candidate",
    "ShowToast(password.Password"
)) {
    if ($controller.Contains($unsafeUiPattern)) {
        throw "Credential value could escape into normal UI: $unsafeUiPattern"
    }
}

foreach ($requiredCredentialPrimitive in @(
    "DataProtectionScope.CurrentUser",
    "WRN-CRED-V1",
    "KEY_VALIDATION_MISMATCH",
    "RandomNumberGenerator.Create",
    "https://openrouter.ai/api/v1/key",
    "SecurityProtocolType.Tls12"
)) {
    if (-not $credentials.Contains($requiredCredentialPrimitive)) {
        throw "Missing credential security primitive: $requiredCredentialPrimitive"
    }
}

if ($credentials.Contains("Console.WriteLine") -or
    $credentials.Contains("Trace.Write") -or
    $credentials.Contains("Debug.Write")) {
    throw "Credential service must not log key-bearing state."
}

Write-Host "PASS: Settings connection surface present"
Write-Host "PASS: masked native PasswordBox entry"
Write-Host "PASS: connect / test / remove actions wired"
Write-Host "PASS: WRN tile routes unconfigured users to Settings"
Write-Host "PASS: credential service uses CurrentUser DPAPI and TLS 1.2 validation"
Write-Host "PASS: no credential-to-clipboard/display path detected"
Write-Host "PASS: live Claude switching remains disconnected"
Write-Host "PHASE4_CREDENTIAL_UI_VERIFY_PASS" -ForegroundColor Green
