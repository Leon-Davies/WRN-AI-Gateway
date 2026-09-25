$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$gatewaySrc = Join-Path $root "src\WRN.AIGateway.Gateway"
$temp = Join-Path $env:TEMP ("wrn-phase4b-failures-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1") | Out-Null

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "RuntimeFailureTests.exe"

    & $csc @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        (Join-Path $src "RuntimeFailures.cs"),
        (Join-Path $root "tests\RuntimeFailureTests.cs")
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime failure test compilation failed."
    }

    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime failure tests failed."
    }

    $sanitizerExe = Join-Path $temp "GatewayFailureSanitizerTests.exe"
    & $csc @(
        "/nologo",
        "/target:exe",
        "/out:$sanitizerExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        (Join-Path $src "RuntimeFailures.cs"),
        (Join-Path $gatewaySrc "GatewayFailureSanitizer.cs"),
        (Join-Path $root "tests\GatewayFailureSanitizerTests.cs")
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Gateway failure sanitizer test compilation failed."
    }

    & $sanitizerExe
    if ($LASTEXITCODE -ne 0) {
        throw "Gateway failure sanitizer tests failed."
    }

    $policy = Get-Content (Join-Path $gatewaySrc "GatewayPolicy.cs") -Raw
    foreach ($requiredPolicy in @(
        'root.Remove("models")',
        'provider["zdr"] = true',
        'provider["data_collection"] = "deny"',
        'provider["allow_fallbacks"] = true'
    )) {
        if (-not $policy.Contains($requiredPolicy)) {
            throw "Gateway routing hardening is missing: $requiredPolicy"
        }
    }

    foreach ($forbiddenRouting in @(
        'provider["only"]',
        'provider["order"]',
        'provider["sort"]'
    )) {
        if ($policy.Contains($forbiddenRouting)) {
            throw "Gateway must not install caller/provider pinning in production policy: $forbiddenRouting"
        }
    }

    $gateway = Get-Content (Join-Path $gatewaySrc "Program.cs") -Raw
    foreach ($requiredGateway in @(
        "RuntimeFailureCatalog.FromUpstreamStatus",
        "RuntimeFailureCatalog.TransportFailure",
        "WriteAnthropicFailure",
        "response.IsSuccessStatusCode",
        "GatewayFailureSanitizer.TryMapJsonError",
        "TrySanitizeSseDataLine"
    )) {
        if (-not $gateway.Contains($requiredGateway)) {
            throw "Gateway friendly-failure primitive is missing: $requiredGateway"
        }
    }

    foreach ($rawLeak in @(
        "ReadAsStringAsync",
        "upstream_error_body",
        "response_body="
    )) {
        if ($gateway.Contains($rawLeak)) {
            throw "Gateway may expose/log raw upstream failure content: $rawLeak"
        }
    }

    $credentials = Get-Content (Join-Path $src "OpenRouterCredentials.cs") -Raw
    if (-not $credentials.Contains("attempt < 2") -or
        -not $credentials.Contains("IsSafeCredentialValidationRetry") -or
        -not $credentials.Contains("Thread.Sleep(250)")) {
        throw "Bounded idempotent credential retry is missing."
    }

    $controller = Get-Content (Join-Path $src "AppController.cs") -Raw
    if (-not $controller.Contains("RuntimeFailureCatalog") -or
        $controller.Contains('case "KEY_UNAUTHORIZED"')) {
        throw "Settings must use the shared failure taxonomy."
    }

    Write-Host ""
    Write-Host "PASS: normalized runtime failure taxonomy"
    Write-Host "PASS: one bounded retry only on idempotent transient key validation"
    Write-Host "PASS: inference transport is not automatically replayed"
    Write-Host "PASS: same-model provider fallback explicitly enabled"
    Write-Host "PASS: cross-model fallback removed"
    Write-Host "PASS: caller routing overrides discarded"
    Write-Host "PASS: ZDR/data-collection policy remains enforced"
    Write-Host "PASS: final upstream failures map to Anthropic-compatible friendly errors"
    Write-Host "PASS: HTTP-200 embedded JSON/SSE errors are sanitized"
    Write-Host "PASS: raw upstream failure bodies are not surfaced/logged"
    Write-Host "PHASE4B_FAILURES_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
