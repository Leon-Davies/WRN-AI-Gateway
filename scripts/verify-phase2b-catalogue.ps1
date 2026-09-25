$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase2b-catalogue-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    foreach ($required in @(
        (Join-Path $dist "catalogue\catalogue.json"),
        (Join-Path $dist "catalogue\catalogue.sig")
    )) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Missing catalogue build output: $required" }
    }

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "CatalogueRuntimeTests.exe"
    $compileArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        (Join-Path $src "ModelCatalogue.cs"),
        (Join-Path $root "tests\CatalogueRuntimeTests.cs")
    )
    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) { throw "Catalogue test compilation failed." }

    $store = Join-Path $temp "store"
    $testArgs = @(
        (Join-Path $dist "catalogue"),
        (Join-Path $root "tests\fixtures\catalogue-v2.json"),
        (Join-Path $root "tests\fixtures\catalogue-v2.sig"),
        $store
    )
    & $testExe $testArgs
    if ($LASTEXITCODE -ne 0) { throw "Catalogue runtime tests failed." }

    $report = Join-Path $temp "catalogue-report.json"
    $proc = Start-Process -FilePath (Join-Path $dist "WRN-AI-Gateway.exe") -ArgumentList @("--catalogue-report", $report) -Wait -PassThru
    if ($proc.ExitCode -ne 0) { throw "Catalogue report command failed." }
    $json = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($json.release -lt 1) { throw "Catalogue report has invalid release." }
    if (@($json.visibleModels).Count -ne 4) { throw "Catalogue report does not expose four models." }

    $source = Get-Content (Join-Path $src "ModelCatalogue.cs") -Raw
    if ($source -match "PRIVATE KEY" -or $source -match "ExportCspBlob\(true\)") {
        throw "Catalogue runtime must never contain private signing material."
    }

    Write-Host ""
    Write-Host ("Catalogue release: " + $json.release)
    Write-Host ("Catalogue source: " + $json.source)
    Write-Host ("Visible models: " + (@($json.visibleModels) -join ", "))
    Write-Host "PHASE2B_CATALOGUE_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
