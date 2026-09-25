$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$temp = Join-Path $env:TEMP ("wrn-phase6-containment-" + [Guid]::NewGuid().ToString("N"))
$ps = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"

New-Item -ItemType Directory -Force -Path $temp | Out-Null

function Invoke-QualifiedVerifier(
    [string]$Name,
    [string]$ScriptName,
    [string]$Marker
) {
    $scriptPath = Join-Path $PSScriptRoot $ScriptName
    $logPath = Join-Path $temp ($Name + ".log")

    & $ps -NoProfile -ExecutionPolicy Bypass -File $scriptPath *> $logPath
    if ($LASTEXITCODE -ne 0) {
        Get-Content $logPath
        throw "$Name verifier failed."
    }

    $text = Get-Content $logPath -Raw
    if (-not $text.Contains($Marker)) {
        Get-Content $logPath
        throw "$Name verifier did not emit $Marker."
    }

    Write-Host ("PASS: " + $Name)
}

try {
    Invoke-QualifiedVerifier "catalogue-integrity" "verify-phase2b-catalogue.ps1" "PHASE2B_CATALOGUE_VERIFY_PASS"
    Invoke-QualifiedVerifier "mode-recovery" "verify-phase3-coordinator.ps1" "PHASE3_COORDINATOR_VERIFY_PASS"
    Invoke-QualifiedVerifier "runtime-failures" "verify-phase4b-failures.ps1" "PHASE4B_FAILURES_VERIFY_PASS"
    Invoke-QualifiedVerifier "app-update-recovery" "verify-phase5-updater.ps1" "PHASE5_UPDATER_VERIFY_PASS"
    Invoke-QualifiedVerifier "diagnostics-privacy" "verify-phase6-diagnostics.ps1" "PHASE6_DIAGNOSTICS_VERIFY_PASS"

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $matrixExe = Join-Path $temp "FailureContainmentMatrixTests.exe"

    & $csc @(
        "/nologo",
        "/target:exe",
        "/out:$matrixExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        (Join-Path $src "RuntimeFailures.cs"),
        (Join-Path $root "tests\FailureContainmentMatrixTests.cs")
    )

    if ($LASTEXITCODE -ne 0) {
        throw "Failure containment matrix compilation failed."
    }

    & $matrixExe
    if ($LASTEXITCODE -ne 0) {
        throw "Failure containment matrix tests failed."
    }

    $controller = Get-Content (Join-Path $src "AppController.cs") -Raw
    $transition = Get-Content (Join-Path $src "ClaudeTransition.cs") -Raw
    $catalogue = Get-Content (Join-Path $src "ModelCatalogue.cs") -Raw

    if (-not $controller.Contains("UserFacingFailureMessages.AppUpdate")) {
        throw "Application update UI is not using shared friendly failure messages."
    }

    if ($controller.Contains("FriendlyAppUpdateMessage")) {
        throw "Legacy private update failure mapper remains in the controller."
    }

    if ($controller.Contains("OpenRouter key before WRN Claude")) {
        throw "WRN launch placeholder exposes unnecessary API-key terminology."
    }

    if (-not $transition.Contains("public const bool LiveClaudeWritesEnabled = false")) {
        throw "Live Claude writes must remain disabled pending managed-Claude qualification."
    }

    foreach ($required in @(
        "CATALOGUE_SIGNATURE_INVALID",
        "CATALOGUE_ROLLBACK_REJECTED",
        "CATALOGUE_RELEASE_REUSE_REJECTED"
    )) {
        if (-not $catalogue.Contains($required)) {
            throw "Catalogue containment primitive is missing: $required"
        }
    }

    Write-Host ""
    Write-Host "PASS: catalogue tamper/rollback failures fail closed with signed fallback"
    Write-Host "PASS: unsupported/recovery Claude state and pending transaction block mode planning"
    Write-Host "PASS: interrupted transition recovery remains rollback-first"
    Write-Host "PASS: OpenRouter/gateway/upstream failures use friendly normalized errors"
    Write-Host "PASS: application update verification/activation failures preserve current version"
    Write-Host "PASS: colleague-facing failure messages hide raw status/exception payloads"
    Write-Host "PASS: privacy-safe diagnostics bundle is available for support"
    Write-Host "PASS: live Claude writes remain qualification-gated"
    Write-Host "PHASE6_FAILURE_CONTAINMENT_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
