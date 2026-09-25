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

    $controllerSource = Get-Content (Join-Path $src "AppController.cs") -Raw
    if ($controllerSource.Contains("ClaudeTransitionExecutor.Execute")) {
        throw "Transition executor must not be wired to the user-facing launch buttons yet."
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

    Write-Host "PASS: fixture-only WTW -> WRN compiler"
    Write-Host "PASS: profile/meta/deployment activation ordering"
    Write-Host "PASS: signed catalogue generates Claude model aliases"
    Write-Host "PASS: stale source preflight rejection"
    Write-Host "PASS: injected failure rollback"
    Write-Host "PASS: unsafe source and profile collisions blocked"
    Write-Host "PASS: actual Claude paths hard-disabled"
    Write-Host "PASS: UI launch buttons remain disconnected"
    Write-Host "PHASE3_TRANSITION_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
