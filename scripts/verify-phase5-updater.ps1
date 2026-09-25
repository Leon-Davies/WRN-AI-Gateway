$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$updaterSrc = Join-Path $root "src\WRN.AIGateway.Updater"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase5-updater-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    if (-not (Test-Path -LiteralPath (Join-Path $dist "WRN-AI-Gateway-Updater.exe"))) {
        throw "Updater helper was not built."
    }

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "AppUpdateTests.exe"

    $compileArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Security.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        ("/reference:" + (Join-Path $framework "System.Xml.dll")),
        ("/reference:" + (Join-Path $framework "System.IO.Compression.dll")),
        ("/reference:" + (Join-Path $framework "System.IO.Compression.FileSystem.dll")),
        (Join-Path $src "ModelCatalogue.cs"),
        (Join-Path $src "AppUpdate.cs"),
        (Join-Path $updaterSrc "UpdateActivation.cs"),
        (Join-Path $root "tests\AppUpdateTests.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "App update fixture compilation failed."
    }

    $fixtureRoot = Join-Path $temp "fixtures"
    & $testExe $dist $fixtureRoot
    if ($LASTEXITCODE -ne 0) {
        throw "App update fixture tests failed."
    }

    & (Join-Path $PSScriptRoot "package.ps1") -Version "0.5.0-beta.1" -Release 1 | Out-Null

    $release = Join-Path $root "release\WRN-AI-Gateway-v0.5.0-beta.1"
    $app = Join-Path $release "app"
    $identity = Get-Content -LiteralPath (Join-Path $app "app-release.json") -Raw | ConvertFrom-Json

    if ([int]$identity.release -ne 1 -or [string]$identity.version -ne "0.5.0-beta.1") {
        throw "Packaged application release identity is invalid."
    }

    foreach ($required in @(
        "WRN-AI-Gateway.exe",
        "WRN-AI-Gateway-Gateway.exe",
        "WRN-AI-Gateway-Updater.exe",
        "app-release.json",
        "ui\MainWindow.xaml",
        "catalogue\catalogue.json",
        "catalogue\catalogue.sig"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $app $required))) {
            throw "Packaged update payload is missing: $required"
        }
    }

    $selfCheckReport = Join-Path $temp "packaged-self-check.json"
    $selfCheck = Start-Process -FilePath (Join-Path $app "WRN-AI-Gateway.exe") -ArgumentList @(
        "--self-check",
        "--self-check-report",
        $selfCheckReport
    ) -Wait -PassThru

    if ($selfCheck.ExitCode -ne 0) {
        throw "Packaged application self-check failed."
    }

    $report = Get-Content -LiteralPath $selfCheckReport -Raw | ConvertFrom-Json
    if ($report.ok -ne $true -or [int]$report.release -ne 1 -or [string]$report.version -ne "0.5.0-beta.1") {
        throw "Packaged self-check report is invalid."
    }

    if (Test-Path -LiteralPath (Join-Path $app "credentials")) {
        throw "Application package must never contain credential state."
    }

    $updateSource = Get-Content (Join-Path $src "AppUpdate.cs") -Raw
    foreach ($requiredPrimitive in @(
        "AppUpdateTrust.PublicKeyXml",
        "VerifyData",
        "UPDATE_ROLLBACK_REJECTED",
        "UPDATE_ARTIFACT_HASH_MISMATCH",
        "Archive path traversal rejected",
        "AppSelfCheck.ValidateDirectory"
    )) {
        if (-not $updateSource.Contains($requiredPrimitive)) {
            throw "Required updater safety primitive is missing: $requiredPrimitive"
        }
    }

    if ($updateSource.Contains("ExportCspBlob($true)") -or
        $updateSource.Contains("ToXmlString(true)")) {
        throw "Client updater source must not contain private signing material."
    }

    $activationSource = Get-Content (Join-Path $updaterSrc "UpdateActivation.cs") -Raw
    foreach ($requiredActivation in @(
        "UPDATE_DEFERRED_RECOVERY_PENDING",
        "transition.lock",
        "UPDATE_ACTIVATION_ROLLED_BACK",
        "FileShare.None"
    )) {
        if (-not $activationSource.Contains($requiredActivation)) {
            throw "Required activation safety primitive is missing: $requiredActivation"
        }
    }

    $runnerSource = Get-Content (Join-Path $updaterSrc "Program.cs") -Raw
    if (-not $runnerSource.Contains('GetProcessesByName(') -or
        -not $runnerSource.Contains('"claude"') -or
        -not $runnerSource.Contains("--self-check")) {
        throw "Updater runner safe-point checks are incomplete."
    }

    Write-Host ""
    Write-Host "PASS: dedicated signed application release manifest"
    Write-Host "PASS: artifact size/hash verification"
    Write-Host "PASS: zip traversal protection"
    Write-Host "PASS: rollback/same-release safety"
    Write-Host "PASS: staged candidate identity + bundled catalogue self-check"
    Write-Host "PASS: current/previous activation preserves WRN durable state"
    Write-Host "PASS: failed post-activation health restores last-known-good"
    Write-Host "PASS: pending Claude transition defers app activation"
    Write-Host "PASS: external updater helper builds"
    Write-Host "PASS: packaged release identity + headless self-check"
    Write-Host "PASS: package contains no credential material"
    Write-Host "PHASE5_UPDATER_CORE_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
