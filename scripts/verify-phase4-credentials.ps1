$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase4-credentials-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "CredentialStoreTests.exe"

    $compileArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Security.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        (Join-Path $src "ClaudeDiscovery.cs"),
        (Join-Path $src "ModelCatalogue.cs"),
        (Join-Path $src "ClaudeTransition.cs"),
        (Join-Path $src "ModeCoordinator.cs"),
        (Join-Path $src "GatewayLifecycle.cs"),
        (Join-Path $src "OpenRouterCredentials.cs"),
        (Join-Path $src "RuntimeFailures.cs"),
        (Join-Path $root "tests\CredentialStoreTests.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Credential store test compilation failed."
    }

    $fixtureRoot = Join-Path $temp "fixtures"
    & $testExe $dist $fixtureRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Credential store fixture tests failed."
    }

    $source = Get-Content (Join-Path $src "OpenRouterCredentials.cs") -Raw
    foreach ($required in @(
        "DataProtectionScope.CurrentUser",
        "WRN-CRED-V1",
        "KEY_VALIDATION_MISMATCH",
        "RandomNumberGenerator.Create",
        "AllocateLoopbackPort"
    )) {
        if (-not $source.Contains($required)) {
            throw "Required credential safety primitive is missing: $required"
        }
    }

    $gatewaySource = Get-Content (Join-Path $root "src\WRN.AIGateway.Gateway\Program.cs") -Raw
    if (-not $gatewaySource.Contains("WRN-CRED-V1")) {
        throw "Gateway does not support the versioned credential envelope."
    }

    Write-Host ""
    Write-Host "PASS: DPAPI credential envelope"
    Write-Host "PASS: validation binding prevents stale/mismatched replacement"
    Write-Host "PASS: replacement preserves local gateway identity"
    Write-Host "PASS: remove leaves reusable gateway config"
    Write-Host "PASS: new envelope starts packaged gateway"
    Write-Host "PASS: legacy raw-DPAPI gateway compatibility retained"
    Write-Host "PHASE4_CREDENTIAL_STORE_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
