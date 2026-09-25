$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$temp = Join-Path $env:TEMP ("wrn-phase2c-publisher-" + [Guid]::NewGuid().ToString("N"))
$maintainerRoot = Join-Path $env:LOCALAPPDATA "WRN-AI-Gateway-Maintainer"
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build-catalogue-check.ps1") | Out-Null
    $checkExe = Join-Path $root "dist\maintainer\WRN-CatalogueCheck.exe"
    $bundledJson = Join-Path $root "src\WRN.AIGateway\catalogue\catalogue.json"
    $bundledSig = Join-Path $root "src\WRN.AIGateway\catalogue\catalogue.sig"

    & $checkExe verify $bundledJson $bundledSig
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled catalogue verification failed."
    }

    $draft = Join-Path $temp "draft.json"
    & (Join-Path $PSScriptRoot "New-WRNCatalogueDraft.ps1") -Destination $draft | Out-Null
    if (-not (Test-Path -LiteralPath $draft)) {
        throw "Draft preparation failed."
    }

    $preview = & (Join-Path $PSScriptRoot "Publish-WRNCatalogue.ps1") -DraftPath $draft
    if ($LASTEXITCODE -ne 0) {
        throw "Publisher preview failed."
    }
    if (-not (($preview -join [Environment]::NewLine).Contains("WRN_CATALOGUE_PREVIEW_READY"))) {
        throw "Publisher preview did not reach ready state."
    }

    $badDraft = Join-Path $temp "unqualified.json"
    $doc = Get-Content -LiteralPath $draft -Raw | ConvertFrom-Json
    $doc.models[0].upstreamModel = "openai/not-a-real-model"
    $encoding = New-Object System.Text.UTF8Encoding($false)
    $badText = ($doc | ConvertTo-Json -Depth 40) + [Environment]::NewLine
    [IO.File]::WriteAllText($badDraft, $badText, $encoding)

    $gateFailed = $false
    try {
        & (Join-Path $PSScriptRoot "Publish-WRNCatalogue.ps1") -DraftPath $badDraft -ChangeTitle "Qualification gate test" -ChangeBody "Must remain unpublished." | Out-Null
    }
    catch {
        if ($_.Exception.Message -match "fresh passing qualification report is required") {
            $gateFailed = $true
        }
    }
    if (-not $gateFailed) {
        throw "Unqualified upstream model change was not blocked."
    }

    $auditPath = Join-Path $maintainerRoot "publication-audit.jsonl"
    if (-not (Test-Path -LiteralPath $auditPath)) {
        throw "Publisher audit log is missing."
    }
    $auditRows = @(Get-Content -LiteralPath $auditPath | ForEach-Object { $_ | ConvertFrom-Json })
    $release4 = @($auditRows | Where-Object { $_.release -eq 4 -and $_.remoteVerified -eq $true })
    if ($release4.Count -lt 1) {
        throw "No verified release-4 publication audit record was found."
    }

    $qualRoot = Join-Path $maintainerRoot "qualifications"
    $lunaReports = @()
    if (Test-Path -LiteralPath $qualRoot) {
        $lunaReports = @(Get-ChildItem -LiteralPath $qualRoot -Filter "openai_gpt-6-luna-*.json" -File | Sort-Object LastWriteTimeUtc -Descending)
    }
    if ($lunaReports.Count -lt 1) {
        throw "No Luna qualification report was found."
    }

    $luna = Get-Content -LiteralPath $lunaReports[0].FullName -Raw | ConvertFrom-Json
    if ($luna.passed -ne $true) {
        throw "Latest Luna qualification report is not passing."
    }

    foreach ($name in @("modelExists", "zdrRouteExists", "inference", "streaming", "toolCall", "toolContinuation")) {
        if ($luna.checks.$name -ne $true) {
            throw "Latest Luna qualification report failed check: $name"
        }
    }
    $publisherSource = Get-Content (Join-Path $PSScriptRoot "Publish-WRNCatalogue.ps1") -Raw
    if (-not $publisherSource.Contains('if (-not $Publish)')) {
        throw "Publisher must default to preview-only mode."
    }
    if (-not $publisherSource.Contains('"catalogue/catalogue.json", "catalogue/catalogue.sig"')) {
        throw "Publisher must stage catalogue JSON and signature together."
    }
    if (-not $publisherSource.Contains("remoteVerified")) {
        throw "Publisher post-publication verification/audit is missing."
    }

    Write-Host "PASS: signed client-equivalent validation"
    Write-Host "PASS: draft preparation"
    Write-Host "PASS: preview-only default"
    Write-Host "PASS: unqualified upstream change blocked"
    Write-Host "PASS: release 4 audit remote-verified"
    Write-Host "PASS: direct OpenRouter Luna qualification"
    Write-Host "PHASE2C_PUBLISHER_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
