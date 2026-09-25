param(
    [Parameter(Mandatory = $true)]
    [string]$KeyEnvFile
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$temp = Join-Path $env:TEMP ("wrn-phase4-live-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1") | Out-Null

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "CredentialLiveValidation.exe"

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
        (Join-Path $root "tests\CredentialLiveValidation.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Live credential validation test compilation failed."
    }

    $stateRoot = Join-Path $temp "state"
    & $testExe $KeyEnvFile $stateRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Live OpenRouter credential validation failed."
    }

    Write-Host "PHASE4_LIVE_CREDENTIAL_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
