$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase6-diagnostics-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "DiagnosticsBundleTests.exe"

    $compileArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Security.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        ("/reference:" + (Join-Path $framework "System.DirectoryServices.dll")),
        ("/reference:" + (Join-Path $framework "System.DirectoryServices.AccountManagement.dll")),
        ("/reference:" + (Join-Path $framework "System.IO.Compression.dll")),
        ("/reference:" + (Join-Path $framework "System.IO.Compression.FileSystem.dll")),
        ("/reference:" + (Join-Path $framework "System.Xml.dll")),
        (Join-Path $src "ClaudeDiscovery.cs"),
        (Join-Path $src "ModelCatalogue.cs"),
        (Join-Path $src "ClaudeTransition.cs"),
        (Join-Path $src "ModeCoordinator.cs"),
        (Join-Path $src "GatewayLifecycle.cs"),
        (Join-Path $src "OpenRouterCredentials.cs"),
        (Join-Path $src "RuntimeFailures.cs"),
        (Join-Path $src "AppUpdate.cs"),
        (Join-Path $src "DiagnosticsBundle.cs"),
        (Join-Path $root "tests\DiagnosticsBundleTests.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Diagnostics bundle test compilation failed."
    }

    $stateRoot = Join-Path $temp "state"
    $outputRoot = Join-Path $temp "output"

    & $testExe $dist $stateRoot $outputRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Diagnostics bundle fixture tests failed."
    }

    $source = Get-Content (Join-Path $src "DiagnosticsBundle.cs") -Raw
    foreach ($forbidden in @(
        "DesktopConfigPath =",
        "MetaPath =",
        "WrnProfilePath =",
        "StoredOpenRouterCredential",
        "ReadAllBytes(CredentialPath"
    )) {
        if ($source.Contains($forbidden)) {
            throw "Diagnostics source crosses privacy boundary: $forbidden"
        }
    }

    $xaml = Get-Content (Join-Path $src "ui\MainWindow.xaml") -Raw
    $controller = Get-Content (Join-Path $src "AppController.cs") -Raw

    if (-not $xaml.Contains('x:Name="CollectDiagnosticsButton"')) {
        throw "Support diagnostics action is missing."
    }

    if (-not $controller.Contains("DiagnosticsBundle.Create")) {
        throw "Support diagnostics action is not wired."
    }

    Write-Host ""
    Write-Host "PASS: diagnostics bundle contains only allowlisted WRN support files"
    Write-Host "PASS: secret-like gateway text is redacted"
    Write-Host "PASS: credentials, prompts, Claude history and file contents are excluded"
    Write-Host "PASS: safe app/discovery/catalogue/gateway/update metadata retained"
    Write-Host "PASS: Support action is wired to privacy-safe diagnostics collection"
    Write-Host "PHASE6_DIAGNOSTICS_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
