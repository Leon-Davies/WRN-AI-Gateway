$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$updaterSrc = Join-Path $root "src\WRN.AIGateway.Updater"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase5-remote-" + [Guid]::NewGuid().ToString("N"))

$manifestUrl = "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/app-update-beta/updates/release.json"
$signatureUrl = "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/app-update-beta/updates/release.sig"

New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1") | Out-Null

    $nonce = [Guid]::NewGuid().ToString("N")
    $headers = @{
        "Cache-Control" = "no-cache"
        "Pragma" = "no-cache"
    }

    $manifestPath = Join-Path $temp "release.json"
    $signaturePath = Join-Path $temp "release.sig"

    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($manifestUrl + "?wrn=" + $nonce) -OutFile $manifestPath -TimeoutSec 30
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($signatureUrl + "?wrn=" + $nonce) -OutFile $signaturePath -TimeoutSec 30

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.release -lt 1) {
        throw "Published app release is invalid."
    }

    $artifactPath = Join-Path $temp "remote-artifact.zip"
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ([string]$manifest.artifactUrl + "?wrn=" + $nonce) -OutFile $artifactPath -TimeoutSec 120

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "AppUpdateRemotePropagationTests.exe"

    & $csc @(
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
        (Join-Path $root "tests\AppUpdateRemotePropagationTests.cs")
    )

    if ($LASTEXITCODE -ne 0) {
        throw "Remote propagation harness compilation failed."
    }

    & $testExe $dist $manifestPath $signaturePath $artifactPath (Join-Path $temp "fixture")
    if ($LASTEXITCODE -ne 0) {
        throw "Remote propagation test failed."
    }

    Write-Host ("REMOTE_RELEASE=" + [int]$manifest.release)
    Write-Host ("REMOTE_VERSION=" + [string]$manifest.version)
    Write-Host "PHASE5_REMOTE_UPDATE_LIVE_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Start-Sleep -Milliseconds 150
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
