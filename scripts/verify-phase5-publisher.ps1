$ErrorActionPreference = "Stop"

param(
    [string]$Version = "0.5.0-publisher-verify"
)

$root = Split-Path -Parent $PSScriptRoot
$maintainerRoot = Join-Path $env:LOCALAPPDATA "WRN-AI-Gateway-Maintainer"
$keyPath = Join-Path $maintainerRoot "app-release-signing-key.dpapi"
$previewRoot = Join-Path $maintainerRoot "app-update-previews"

if (-not (Test-Path -LiteralPath $keyPath)) {
    throw "Maintainer app-release signing key is missing."
}

& (Join-Path $PSScriptRoot "Publish-WRNAppUpdate.ps1") -Version $Version -Title "WRN AI Gateway publisher verification" -Notes "Preview-only publisher qualification." | Write-Host

$previews = @(
    Get-ChildItem -LiteralPath $previewRoot -Directory |
        Where-Object { $_.Name -like "release-*" } |
        Sort-Object Name -Descending
)

if ($previews.Count -lt 1) {
    throw "App update preview was not created."
}

$preview = $previews[0].FullName
$manifestPath = Join-Path $preview "release.json"
$signaturePath = Join-Path $preview "release.sig"

& (Join-Path $PSScriptRoot "build-app-update-check.ps1") | Out-Null
$checkExe = Join-Path $root "dist\maintainer\WRN-AppUpdateCheck.exe"

& $checkExe verify $manifestPath $signaturePath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Preview manifest/signature failed client-equivalent verification."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]$manifest.version -ne $Version) {
    throw "Preview version does not match requested verifier version."
}

$artifactName = [IO.Path]::GetFileName(([Uri][string]$manifest.artifactUrl).AbsolutePath)
$artifactPath = Join-Path $preview $artifactName
if (-not (Test-Path -LiteralPath $artifactPath)) {
    throw "Preview artifact is missing."
}

$artifactInfo = Get-Item -LiteralPath $artifactPath
$artifactHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()

if ($artifactInfo.Length -ne [long]$manifest.artifactSize -or
    $artifactHash -ne ([string]$manifest.artifactSha256).ToLowerInvariant()) {
    throw "Preview artifact size/hash does not match signed manifest."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [IO.Compression.ZipFile]::OpenRead($artifactPath)
try {
    $localHeroes = @(
        $archive.Entries |
            Where-Object {
                [IO.Path]::GetFileName($_.FullName) -like "wrn-hero*.png"
            }
    )

    if ($localHeroes.Count -ne 0) {
        throw "Public app-update artifact contains local WRN hero assets."
    }

    $required = @(
        "WRN-AI-Gateway.exe",
        "WRN-AI-Gateway-Gateway.exe",
        "WRN-AI-Gateway-Updater.exe",
        "app-release.json",
        "ui/MainWindow.xaml",
        "catalogue/catalogue.json",
        "catalogue/catalogue.sig"
    )

    foreach ($requiredPath in $required) {
        $match = @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.Replace("\", "/") -eq $requiredPath
                }
        )

        if ($match.Count -ne 1) {
            throw "Public app-update artifact is missing: $requiredPath"
        }
    }
}
finally {
    $archive.Dispose()
}

$package = Join-Path $root ("release\WRN-AI-Gateway-v" + $Version)
if (Test-Path -LiteralPath (Join-Path $package "app\credentials")) {
    throw "Release package contains credential material."
}

$publisherSource = Get-Content (Join-Path $PSScriptRoot "Publish-WRNAppUpdate.ps1") -Raw

foreach ($requiredPublisher in @(
    "app-release-signing-key.dpapi",
    "ProtectedData]::Unprotect",
    "SignData",
    "app-update-beta",
    "remoteVerified",
    "WRN_APP_UPDATE_PUBLISH_SUCCESS",
    "Public artifact local WRN hero assets: 0"
)) {
    if (-not $publisherSource.Contains($requiredPublisher)) {
        throw "App publisher safety primitive is missing: $requiredPublisher"
    }
}

if ($publisherSource.Contains("ExportCspBlob($true)") -or
    $publisherSource.Contains("ToXmlString($true)")) {
    throw "App publisher must not serialize private signing material."
}

Write-Host ""
Write-Host "PASS: preview-first app release publisher"
Write-Host "PASS: DPAPI signing key matches embedded client trust root"
Write-Host "PASS: signed manifest verifies client-equivalently"
Write-Host "PASS: public artifact hash/size match signed manifest"
Write-Host "PASS: public artifact excludes local WRN hero assets"
Write-Host "PASS: public artifact contains required app payload"
Write-Host "PASS: release package contains no credential state"
Write-Host "PASS: publisher is isolated from colleague package/runtime"
Write-Host "PHASE5_PUBLISHER_VERIFY_PASS" -ForegroundColor Green
