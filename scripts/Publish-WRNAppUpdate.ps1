param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Title = "WRN AI Gateway update",
    [string]$Notes = "Improvements and reliability updates.",

    [int]$TargetRelease = 0,

    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$repoUrl = "https://github.com/Leon-Davies/WRN-AI-Gateway.git"
$branch = "app-update-beta"
$rawBase = "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/app-update-beta/updates"

$maintainerRoot = Join-Path $env:LOCALAPPDATA "WRN-AI-Gateway-Maintainer"
$publisherRepo = Join-Path $maintainerRoot "app-update-repo"
$keyPath = Join-Path $maintainerRoot "app-release-signing-key.dpapi"
$previewRoot = Join-Path $maintainerRoot "app-update-previews"
$auditPath = Join-Path $maintainerRoot "app-update-publication-audit.jsonl"

function Invoke-Git {
    param([string[]]$Arguments)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & wsl.exe -- git @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0) {
        throw ("Git command failed: git " + ($Arguments -join " ") + " :: " + ($output -join " | "))
    }

    return $output
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Copy-Directory {
    param([string]$Source, [string]$Destination)

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    foreach ($file in Get-ChildItem -LiteralPath $Source -File) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force
    }

    foreach ($directory in Get-ChildItem -LiteralPath $Source -Directory) {
        Copy-Directory -Source $directory.FullName -Destination (Join-Path $Destination $directory.Name)
    }
}

if ([string]::IsNullOrWhiteSpace($Version) -or $Version.Length -gt 64) {
    throw "Version is required and must be 64 characters or fewer."
}
if ([string]::IsNullOrWhiteSpace($Title) -or $Title.Length -gt 160) {
    throw "Title is required and must be 160 characters or fewer."
}
if ($Notes.Length -gt 4000) {
    throw "Notes must be 4000 characters or fewer."
}
if (-not (Test-Path -LiteralPath $keyPath)) {
    throw "Maintainer app-release signing key is missing: $keyPath"
}

New-Item -ItemType Directory -Force -Path $maintainerRoot | Out-Null
New-Item -ItemType Directory -Force -Path $previewRoot | Out-Null

if ($publisherRepo -notmatch '^([A-Za-z]):\\(.*)$') {
    throw "Publisher workspace must be on a Windows drive that WSL can mount."
}
$publisherRepoGit = "/mnt/" + $matches[1].ToLowerInvariant() + "/" + ($matches[2] -replace '\\', '/')

& (Join-Path $PSScriptRoot "build-app-update-check.ps1") | Out-Null
$checkExe = Join-Path $root "dist\maintainer\WRN-AppUpdateCheck.exe"
if (-not (Test-Path -LiteralPath $checkExe)) {
    throw "App update check tool was not built."
}

if (-not (Test-Path -LiteralPath (Join-Path $publisherRepo ".git"))) {
    Invoke-Git @("clone", "--branch", $branch, "--single-branch", $repoUrl, $publisherRepoGit) | Out-Null
}

Invoke-Git @("-C", $publisherRepoGit, "config", "core.autocrlf", "false") | Out-Null

$dirty = & wsl.exe -- git -C $publisherRepoGit status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect app-update publisher workspace."
}
if ($dirty) {
    throw "App-update publisher workspace contains local changes. Resolve them before publishing."
}

Invoke-Git @("-C", $publisherRepoGit, "fetch", "origin", $branch) | Out-Null
Invoke-Git @("-C", $publisherRepoGit, "checkout", $branch) | Out-Null
Invoke-Git @("-C", $publisherRepoGit, "reset", "--hard", "origin/$branch") | Out-Null

$updatesDir = Join-Path $publisherRepo "updates"
$currentManifestPath = Join-Path $updatesDir "release.json"
$currentSignaturePath = Join-Path $updatesDir "release.sig"

$currentRelease = 0
if (Test-Path -LiteralPath $currentManifestPath) {
    if (-not (Test-Path -LiteralPath $currentSignaturePath)) {
        throw "Current app release signature is missing."
    }

    & $checkExe verify $currentManifestPath $currentSignaturePath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Current published app release failed client-equivalent verification."
    }

    $current = Get-Content -LiteralPath $currentManifestPath -Raw | ConvertFrom-Json
    $currentRelease = [int]$current.release
}

if ($TargetRelease -lt 0) {
    throw "TargetRelease must be zero (automatic) or a positive release number."
}

if ($TargetRelease -gt 0) {
    if ($TargetRelease -le $currentRelease) {
        throw ("TargetRelease must be greater than the currently published release {0}." -f $currentRelease)
    }
    $nextRelease = $TargetRelease
}
else {
    $nextRelease = $currentRelease + 1
}

$previewDir = Join-Path $previewRoot ("release-" + $nextRelease.ToString("D8"))

if (Test-Path -LiteralPath $previewDir) {
    Remove-Item -LiteralPath $previewDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $previewDir | Out-Null

& (Join-Path $PSScriptRoot "package.ps1") -Version $Version -AppRelease $nextRelease | Out-Null

# package.ps1 rebuilds dist from scratch, so recreate the maintainer-only
# client-equivalent verifier after packaging.
& (Join-Path $PSScriptRoot "build-app-update-check.ps1") | Out-Null
if (-not (Test-Path -LiteralPath $checkExe)) {
    throw "App update check tool was not rebuilt after packaging."
}

$builtRelease = Join-Path $root ("release\WRN-AI-Gateway-v" + $Version)
$builtApp = Join-Path $builtRelease "app"
if (-not (Test-Path -LiteralPath (Join-Path $builtApp "WRN-AI-Gateway.exe"))) {
    throw "Packaged application payload is missing."
}

& $checkExe self-check $builtApp | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Packaged application failed client-equivalent self-check."
}

$publicApp = Join-Path $previewDir "public-app"
Copy-Directory -Source $builtApp -Destination $publicApp

$publicAssets = Join-Path $publicApp "assets"
if (Test-Path -LiteralPath $publicAssets) {
    Get-ChildItem -LiteralPath $publicAssets -File -Filter "wrn-hero*.png" |
        Remove-Item -Force
}

$remainingLocalHeroes = @(
    Get-ChildItem -LiteralPath $publicApp -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "wrn-hero*.png"
        }
)
if ($remainingLocalHeroes.Count -ne 0) {
    throw "Public update payload still contains local WRN hero assets."
}

& $checkExe self-check $publicApp | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Public-safe application payload failed self-check."
}

$artifactName = "release-" + $nextRelease.ToString("D8") + ".zip"
$artifactPath = Join-Path $previewDir $artifactName
if (Test-Path -LiteralPath $artifactPath) {
    Remove-Item -LiteralPath $artifactPath -Force
}

Compress-Archive -Path (Join-Path $publicApp "*") -DestinationPath $artifactPath -CompressionLevel Optimal

$artifactInfo = Get-Item -LiteralPath $artifactPath
$artifactHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    schemaVersion = 1
    release = $nextRelease
    version = $Version
    publishedAt = [DateTimeOffset]::Now.ToString("o")
    artifactUrl = $rawBase + "/artifacts/" + $artifactName
    artifactSha256 = $artifactHash
    artifactSize = [long]$artifactInfo.Length
    title = $Title
    notes = $Notes
}

$manifestPath = Join-Path $previewDir "release.json"
$signaturePath = Join-Path $previewDir "release.sig"
$manifestText = $manifest | ConvertTo-Json -Depth 10
Write-Utf8NoBom -Path $manifestPath -Text ($manifestText + [Environment]::NewLine)

Add-Type -AssemblyName System.Security

$protected = [Convert]::FromBase64String(
    (Get-Content -LiteralPath $keyPath -Raw).Trim())
$privateBlob = [System.Security.Cryptography.ProtectedData]::Unprotect(
    $protected,
    $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser)

try {
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    try {
        $rsa.ImportCspBlob($privateBlob)

        $derivedPublic = $rsa.ToXmlString($false).Trim()
        $expectedPublic = (& $checkExe public-key | Out-String).Trim()

        if ($derivedPublic -ne $expectedPublic) {
            throw "Maintainer app-release signing key does not match the client verification key."
        }

        $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
        $signature = $rsa.SignData(
            $manifestBytes,
            [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))

        try {
            [IO.File]::WriteAllText(
                $signaturePath,
                [Convert]::ToBase64String($signature),
                [Text.Encoding]::ASCII)
        }
        finally {
            [Array]::Clear($signature, 0, $signature.Length)
        }
    }
    finally {
        $rsa.Dispose()
    }
}
finally {
    [Array]::Clear($privateBlob, 0, $privateBlob.Length)
    [Array]::Clear($protected, 0, $protected.Length)
}

& $checkExe verify $manifestPath $signaturePath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Signed app release manifest failed verification."
}

$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Output "WRN_APP_UPDATE_PREVIEW_READY"
Write-Output ("Release: " + $nextRelease)
Write-Output ("Version: " + $Version)
Write-Output ("Manifest SHA-256: " + $manifestHash)
Write-Output ("Artifact SHA-256: " + $artifactHash)
Write-Output ("Artifact bytes: " + $artifactInfo.Length)
Write-Output ("Preview: " + $previewDir)
Write-Output "Public artifact local WRN hero assets: 0"

if (-not $Publish) {
    Write-Output "Preview only. Re-run with -Publish to publish this release."
    exit 0
}

Invoke-Git @("-C", $publisherRepoGit, "fetch", "origin", $branch) | Out-Null
$localHead = (& wsl.exe -- git -C $publisherRepoGit rev-parse HEAD).Trim()
$remoteHead = (& wsl.exe -- git -C $publisherRepoGit rev-parse "origin/$branch").Trim()

if ($localHead -ne $remoteHead) {
    throw "App-update branch changed after preview preparation. Re-run publication from the new current release."
}

New-Item -ItemType Directory -Force -Path (Join-Path $updatesDir "artifacts") | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination $currentManifestPath -Force
Copy-Item -LiteralPath $signaturePath -Destination $currentSignaturePath -Force
Copy-Item -LiteralPath $artifactPath -Destination (Join-Path $updatesDir ("artifacts\" + $artifactName)) -Force

& $checkExe verify $currentManifestPath $currentSignaturePath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Publisher workspace app release failed verification."
}

Invoke-Git @(
    "-C", $publisherRepoGit,
    "add",
    "updates/release.json",
    "updates/release.sig",
    ("updates/artifacts/" + $artifactName)
) | Out-Null

Invoke-Git @(
    "-C", $publisherRepoGit,
    "commit",
    "-m",
    ("app-update: publish release " + $nextRelease + " - " + $Version)
) | Out-Null

$commitSha = (& wsl.exe -- git -C $publisherRepoGit rev-parse HEAD).Trim()
Invoke-Git @("-C", $publisherRepoGit, "push", "origin", $branch) | Out-Null

$remoteManifest = Join-Path $previewDir "remote-release.json"
$remoteSignature = Join-Path $previewDir "remote-release.sig"
$remoteArtifact = Join-Path $previewDir ("remote-" + $artifactName)
$remoteVerified = $false

for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        $nonce = [Guid]::NewGuid().ToString("N")
        $headers = @{
            "Cache-Control" = "no-cache"
            "Pragma" = "no-cache"
        }

        Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($rawBase + "/release.json?wrn=" + $nonce) -OutFile $remoteManifest -TimeoutSec 30
        Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($rawBase + "/release.sig?wrn=" + $nonce) -OutFile $remoteSignature -TimeoutSec 30
        Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($rawBase + "/artifacts/" + $artifactName + "?wrn=" + $nonce) -OutFile $remoteArtifact -TimeoutSec 120

        $remoteManifestHash = (Get-FileHash -LiteralPath $remoteManifest -Algorithm SHA256).Hash.ToLowerInvariant()
        $remoteArtifactHash = (Get-FileHash -LiteralPath $remoteArtifact -Algorithm SHA256).Hash.ToLowerInvariant()

        if ($remoteManifestHash -eq $manifestHash -and
            $remoteArtifactHash -eq $artifactHash -and
            (Get-Item -LiteralPath $remoteArtifact).Length -eq $artifactInfo.Length) {

            & $checkExe verify $remoteManifest $remoteSignature | Out-Null
            if ($LASTEXITCODE -eq 0) {
                $remoteDoc = Get-Content -LiteralPath $remoteManifest -Raw | ConvertFrom-Json
                if ([int]$remoteDoc.release -eq $nextRelease -and
                    [string]$remoteDoc.version -eq $Version) {
                    $remoteVerified = $true
                    break
                }
            }
        }
    }
    catch {
    }

    Start-Sleep -Seconds 2
}

$audit = [ordered]@{
    publishedAt = [DateTimeOffset]::UtcNow.ToString("o")
    release = $nextRelease
    version = $Version
    commit = $commitSha
    manifestSha256 = $manifestHash
    artifactSha256 = $artifactHash
    artifactSize = [long]$artifactInfo.Length
    remoteVerified = $remoteVerified
}

Write-Utf8NoBom -Path (Join-Path $previewDir "publication-audit.json") -Text (($audit | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
Add-Content -LiteralPath $auditPath -Value ($audit | ConvertTo-Json -Compress -Depth 10) -Encoding UTF8

if (-not $remoteVerified) {
    throw "Published app release could not be verified from the public distribution endpoint."
}

Write-Output "WRN_APP_UPDATE_PUBLISH_SUCCESS"
Write-Output ("Release: " + $nextRelease)
Write-Output ("Version: " + $Version)
Write-Output ("Commit: " + $commitSha)
Write-Output ("Remote manifest SHA-256: " + $manifestHash)
Write-Output ("Remote artifact SHA-256: " + $artifactHash)
