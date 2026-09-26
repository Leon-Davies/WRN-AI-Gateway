$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase3-coordinator-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

function Get-FileState {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return "MISSING"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "ModeCoordinatorTests.exe"

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
        (Join-Path $root "tests\ModeCoordinatorTests.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Mode coordinator test compilation failed."
    }

    $fixtureRoot = Join-Path $temp "fixtures"
    & $testExe $dist $fixtureRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Mode coordinator fixture tests failed."
    }

    $wrnProfileId = "a179a3b8-7f6e-4c80-9e33-3ed210fe3d41"
    $paths = @(
        (Join-Path $env:LOCALAPPDATA "Claude-3p\claude_desktop_config.json"),
        (Join-Path $env:LOCALAPPDATA "Claude-3p\configLibrary\_meta.json"),
        (Join-Path $env:LOCALAPPDATA ("Claude-3p\configLibrary\" + $wrnProfileId + ".json"))
    )

    $before = @{}
    foreach ($path in $paths) {
        $before[$path] = Get-FileState $path
    }

    $reportPath = Join-Path $temp "current-machine-preflight.json"
    $proc = Start-Process -FilePath (Join-Path $dist "WRN-AI-Gateway.exe") -ArgumentList @(
        "--mode-preflight-report",
        "wrn",
        $reportPath
    ) -Wait -PassThru

    if ($proc.ExitCode -ne 0) {
        throw "Current-machine preflight command failed."
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.LiveExecutionEnabled -ne $false -or $report.LiveExecutionAllowed -ne $false) {
        throw "Live execution unexpectedly became enabled."
    }

    if ($report.Discovery.InstallKindName -eq "RecoveryDiagnostic" -and $report.PreflightCompatible -ne $false) {
        throw "Recovery diagnostic installation was not blocked."
    }

    foreach ($path in $paths) {
        $after = Get-FileState $path
        if ($after -ne $before[$path]) {
            throw "Read-only preflight changed Claude configuration: $path"
        }
    }

    $controller = Get-Content (Join-Path $src "AppController.cs") -Raw
    foreach ($requiredLaunchPrimitive in @(
        "BeginClaudeLaunch",
        "ClaudeTransitionExecutor.Execute",
        "GatewayLifecycle.EnsureHealthy"
    )) {
        if (-not $controller.Contains($requiredLaunchPrimitive)) {
            throw "User-facing mode launch wiring is incomplete: $requiredLaunchPrimitive"
        }
    }

    $coordinator = Get-Content (Join-Path $src "ModeCoordinator.cs") -Raw
    if ($coordinator.Contains("LiveClaudeWritesEnabled = true")) {
        throw "Coordinator must not enable live Claude writes."
    }

    Write-Host ""
    Write-Host ("Current install: " + $report.Discovery.InstallKindName)
    Write-Host ("Current mode: " + $report.Discovery.ModeName)
    Write-Host ("Preflight compatible: " + $report.PreflightCompatible)
    Write-Host ("Preflight block: " + $report.PreflightBlockReason)
    Write-Host "PASS: healthy fixture compiles WRN and WTW preflight plans"
    Write-Host "PASS: credentials/catalogue/gateway prerequisites are fail-closed"
    Write-Host "PASS: pending recovery blocks planning and is recoverable"
    Write-Host "PASS: current-machine preflight is read-only"
    Write-Host "PASS: live execution remains hard-disabled"
    Write-Host "PASS: launch buttons are wired behind the hard live-write gate"
    Write-Host "PHASE3_COORDINATOR_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
