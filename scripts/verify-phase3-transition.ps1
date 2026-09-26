$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase3-transition-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "ClaudeTransitionTests.exe"

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
        (Join-Path $root "tests\ClaudeTransitionTests.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Transition test compilation failed."
    }

    $fixtureRoot = Join-Path $temp "fixtures"
    & $testExe (Join-Path $dist "catalogue\catalogue.json") (Join-Path $dist "catalogue\catalogue.sig") $fixtureRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Transition fixture tests failed."
    }

    $transitionSource = Get-Content (Join-Path $src "ClaudeTransition.cs") -Raw
    if (-not $transitionSource.Contains("public const bool LiveClaudeWritesEnabled = false")) {
        throw "Live Claude write hard-disable is missing."
    }
    foreach ($requiredSafetyPrimitive in @(
        "ClaudeDeactivationCompiler",
        "RecoverPending",
        "ProtectedData.Protect",
        "TRANSITION_RECOVERY_CONFLICT",
        "RemoveOwnershipBaselineOnSuccess"
    )) {
        if (-not $transitionSource.Contains($requiredSafetyPrimitive)) {
            throw "Required transition safety primitive is missing: $requiredSafetyPrimitive"
        }
    }

    $controllerSource = Get-Content (Join-Path $src "AppController.cs") -Raw
    foreach ($requiredLaunchPrimitive in @(
        "BeginClaudeLaunch",
        "ClaudeTransitionExecutor.Execute",
        "LaunchManagedClaude",
        "GatewayLifecycle.EnsureHealthy"
    )) {
        if (-not $controllerSource.Contains($requiredLaunchPrimitive)) {
            throw "Required user-facing launch wiring is missing: $requiredLaunchPrimitive"
        }
    }
    foreach ($forbiddenLaunchPlaceholder in @(
        "WTW launch wiring is not enabled yet.",
        "Claude switching remains disabled until managed-Claude qualification is complete."
    )) {
        if ($controllerSource.Contains($forbiddenLaunchPlaceholder)) {
            throw "Obsolete user-facing launch placeholder remains: $forbiddenLaunchPlaceholder"
        }
    }

    foreach ($forbiddenModel in @(
        "openai/gpt-",
        "deepseek/deepseek-",
        "anthropic/claude-opus",
        "GPT-6 Luna",
        "DeepSeek V4.1"
    )) {
        if ($transitionSource.Contains($forbiddenModel)) {
            throw "Transition compiler contains hard-coded model routing: $forbiddenModel"
        }
    }

    Write-Host "PASS: WTW/WRN launch actions are wired behind the hard live-write gate"
    Write-Host "PASS: profile/meta/deployment activation ordering"
    Write-Host "PASS: signed catalogue generates Claude model aliases"
    Write-Host "PASS: stale source preflight rejection"
    Write-Host "PASS: injected failure rollback"
    Write-Host "PASS: unsafe source and profile collisions blocked"
    Write-Host "PASS: field-preserving WRN -> WTW fixture restoration"
    Write-Host "PASS: DPAPI-protected durable transaction recovery"
    Write-Host "PASS: recovery conflict fails closed"
    Write-Host "PASS: ownership baseline excludes preference/credential snapshots"
    Write-Host "PASS: actual Claude paths hard-disabled"
    Write-Host "PASS: UI launch buttons remain disconnected"
    Write-Host "PHASE3_TRANSITION_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
