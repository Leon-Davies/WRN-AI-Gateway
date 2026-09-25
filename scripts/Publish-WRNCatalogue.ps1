param(
    [Parameter(Mandatory = $true)]
    [string]$DraftPath,

    [string]$ChangeTitle,
    [string]$ChangeBody,

    [string[]]$QualificationReport,

    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$repoUrl = "https://github.com/Leon-Davies/WRN-AI-Gateway.git"
$branch = "catalogue-beta"
$rawBase = "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/catalogue-beta/catalogue"
$maintainerRoot = Join-Path $env:LOCALAPPDATA "WRN-AI-Gateway-Maintainer"
$publisherRepo = Join-Path $maintainerRoot "catalogue-repo"
$keyPath = Join-Path $maintainerRoot "catalogue-signing-key.dpapi"
$auditPath = Join-Path $maintainerRoot "publication-audit.jsonl"
$previewRoot = Join-Path $maintainerRoot "previews"

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

function Get-ModelMap {
    param($Models)
    $map = @{}
    foreach ($model in @($Models)) {
        $map[[string]$model.key] = $model
    }
    return $map
}

function Test-QualificationReports {
    param([string[]]$RequiredModelIds, [string[]]$Reports)

    if (@($RequiredModelIds).Count -eq 0) {
        return
    }

    $qualified = @{}
    foreach ($path in @($Reports)) {
        if (-not $path) { continue }
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Qualification report not found: $path"
        }

        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($report.passed -eq $true -and $report.modelId) {
            $qualified[[string]$report.modelId] = $report
        }
    }

    foreach ($modelId in $RequiredModelIds) {
        if (-not $qualified.ContainsKey($modelId)) {
            throw "A fresh passing qualification report is required for upstream model: $modelId"
        }

        $checkedAt = [DateTimeOffset]::Parse([string]$qualified[$modelId].checkedAt)
        if ($checkedAt -lt [DateTimeOffset]::UtcNow.AddDays(-30)) {
            throw "Qualification report is older than 30 days for upstream model: $modelId"
        }
    }
}

if (-not (Test-Path -LiteralPath $DraftPath)) {
    throw "Draft not found: $DraftPath"
}
if (-not (Test-Path -LiteralPath $keyPath)) {
    throw "Maintainer signing key is missing: $keyPath"
}
if ([bool]$ChangeTitle -xor [bool]$ChangeBody) {
    throw "ChangeTitle and ChangeBody must be supplied together."
}

New-Item -ItemType Directory -Force -Path $maintainerRoot | Out-Null
New-Item -ItemType Directory -Force -Path $previewRoot | Out-Null
if ($publisherRepo -notmatch '^([A-Za-z]):\\(.*)$') {
    throw "Publisher workspace must be on a Windows drive that WSL can mount."
}
$publisherRepoGit = "/mnt/" + $matches[1].ToLowerInvariant() + "/" + ($matches[2] -replace '\\', '/')

& (Join-Path $PSScriptRoot "build-catalogue-check.ps1") | Out-Null
$checkExe = Join-Path $root "dist\maintainer\WRN-CatalogueCheck.exe"
if (-not (Test-Path -LiteralPath $checkExe)) {
    throw "Catalogue check tool was not built."
}

if (-not (Test-Path -LiteralPath (Join-Path $publisherRepo ".git"))) {
    Invoke-Git @("clone", "--branch", $branch, "--single-branch", $repoUrl, $publisherRepoGit) | Out-Null
}

Invoke-Git @("-C", $publisherRepoGit, "config", "core.autocrlf", "false") | Out-Null
$dirty = & wsl.exe -- git -C $publisherRepoGit status --porcelain
if ($LASTEXITCODE -ne 0) { throw "Could not inspect publisher workspace." }
if ($dirty) {
    throw "Publisher workspace contains local changes. Resolve them before publishing."
}

Invoke-Git @("-C", $publisherRepoGit, "fetch", "origin", $branch) | Out-Null
Invoke-Git @("-C", $publisherRepoGit, "checkout", $branch) | Out-Null
Invoke-Git @("-C", $publisherRepoGit, "reset", "--hard", "origin/$branch") | Out-Null

$currentPath = Join-Path $publisherRepo "catalogue\catalogue.json"
$currentSigPath = Join-Path $publisherRepo "catalogue\catalogue.sig"
$current = Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json
$draft = Get-Content -LiteralPath $DraftPath -Raw | ConvertFrom-Json

$currentModelJson = @($current.models) | ConvertTo-Json -Depth 30 -Compress
$draftModelJson = @($draft.models) | ConvertTo-Json -Depth 30 -Compress
$modelsChanged = $currentModelJson -ne $draftModelJson

if ($modelsChanged -and -not $ChangeTitle) {
    throw "Model catalogue content changed. Supply ChangeTitle and ChangeBody for the user-visible changelog."
}

$currentMap = Get-ModelMap $current.models
$requiresQualification = New-Object System.Collections.Generic.List[string]
foreach ($model in @($draft.models)) {
    $key = [string]$model.key
    $upstream = [string]$model.upstreamModel
    $needs = $false

    if (-not $currentMap.ContainsKey($key)) {
        $needs = $true
    }
    else {
        $before = $currentMap[$key]
        if ([string]$before.upstreamModel -ne $upstream) {
            $needs = $true
        }
        if ($before.visible -ne $true -and $model.visible -eq $true) {
            $needs = $true
        }
    }

    if ($needs -and -not $requiresQualification.Contains($upstream)) {
        $requiresQualification.Add($upstream)
    }
}

Test-QualificationReports -RequiredModelIds $requiresQualification.ToArray() -Reports $QualificationReport

$nextRelease = [int]$current.release + 1
$draft.release = $nextRelease
$draft.publishedAt = [DateTimeOffset]::Now.ToString("o")

if ($ChangeTitle) {
    $change = [PSCustomObject][ordered]@{
        date = (Get-Date -Format "yyyy-MM-dd")
        title = $ChangeTitle
        body = $ChangeBody
    }
    $draft.changelog = @($change) + @($draft.changelog)
}

$previewDir = Join-Path $previewRoot ("release-" + $nextRelease.ToString("D8"))
if (Test-Path -LiteralPath $previewDir) {
    Remove-Item -LiteralPath $previewDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $previewDir | Out-Null

$candidatePath = Join-Path $previewDir "catalogue.json"
$signaturePath = Join-Path $previewDir "catalogue.sig"
$jsonText = $draft | ConvertTo-Json -Depth 40
Write-Utf8NoBom -Path $candidatePath -Text ($jsonText + [Environment]::NewLine)

& $checkExe validate $candidatePath
if ($LASTEXITCODE -ne 0) {
    throw "Candidate catalogue failed client-equivalent validation."
}

Add-Type -AssemblyName System.Security
$protected = [Convert]::FromBase64String((Get-Content -LiteralPath $keyPath -Raw).Trim())
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
            throw "Maintainer private signing key does not match the client verification key."
        }

        $bytes = [IO.File]::ReadAllBytes($candidatePath)
        $signature = $rsa.SignData(
            $bytes,
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

& $checkExe verify $candidatePath $signaturePath
if ($LASTEXITCODE -ne 0) {
    throw "Signed candidate failed verification."
}

$candidateHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
$visibleModels = @($draft.models | Where-Object { $_.visible -eq $true } | ForEach-Object { $_.label })

Write-Output "WRN_CATALOGUE_PREVIEW_READY"
Write-Output ("Release: " + $nextRelease)
Write-Output ("Visible models: " + ($visibleModels -join ", "))
Write-Output ("Candidate SHA-256: " + $candidateHash)
Write-Output ("Preview: " + $previewDir)
if ($requiresQualification.Count -gt 0) {
    Write-Output ("Fresh qualification required/provided for: " + ($requiresQualification -join ", "))
}

if (-not $Publish) {
    Write-Output "Preview only. Re-run with -Publish to publish this draft."
    exit 0
}

Invoke-Git @("-C", $publisherRepoGit, "fetch", "origin", $branch) | Out-Null
$localHead = (& wsl.exe -- git -C $publisherRepoGit rev-parse HEAD).Trim()
$remoteHead = (& wsl.exe -- git -C $publisherRepoGit rev-parse "origin/$branch").Trim()
if ($localHead -ne $remoteHead) {
    throw "Catalogue branch changed after preview preparation. Re-run publication from the new current release."
}

Copy-Item -LiteralPath $candidatePath -Destination $currentPath -Force
Copy-Item -LiteralPath $signaturePath -Destination $currentSigPath -Force

& $checkExe verify $currentPath $currentSigPath
if ($LASTEXITCODE -ne 0) {
    throw "Publisher workspace copy failed verification."
}

Invoke-Git @("-C", $publisherRepoGit, "add", "catalogue/catalogue.json", "catalogue/catalogue.sig") | Out-Null
$commitMessage = "catalogue: publish release $nextRelease"
if ($ChangeTitle) {
    $commitMessage += " - " + $ChangeTitle
}
Invoke-Git @("-C", $publisherRepoGit, "commit", "-m", $commitMessage) | Out-Null
$commitSha = (& wsl.exe -- git -C $publisherRepoGit rev-parse HEAD).Trim()
Invoke-Git @("-C", $publisherRepoGit, "push", "origin", $branch) | Out-Null

$remoteVerified = $false
$remoteJson = Join-Path $previewDir "remote-catalogue.json"
$remoteSig = Join-Path $previewDir "remote-catalogue.sig"
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        $nonce = [Guid]::NewGuid().ToString("N")
        $headers = @{
            "Cache-Control" = "no-cache"
            "Pragma" = "no-cache"
        }
        Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($rawBase + "/catalogue.json?wrn=" + $nonce) -OutFile $remoteJson -TimeoutSec 30
        Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri ($rawBase + "/catalogue.sig?wrn=" + $nonce) -OutFile $remoteSig -TimeoutSec 30

        $remoteHash = (Get-FileHash -LiteralPath $remoteJson -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($remoteHash -eq $candidateHash) {
            & $checkExe verify $remoteJson $remoteSig | Out-Null
            if ($LASTEXITCODE -eq 0) {
                $remoteDoc = Get-Content -LiteralPath $remoteJson -Raw | ConvertFrom-Json
                if ([int]$remoteDoc.release -eq $nextRelease) {
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

$audit = [PSCustomObject][ordered]@{
    publishedAt = [DateTimeOffset]::UtcNow.ToString("o")
    release = $nextRelease
    commit = $commitSha
    catalogueSha256 = $candidateHash
    remoteVerified = $remoteVerified
    visibleModels = $visibleModels
    qualificationModels = @($requiresQualification)
}
$auditLine = $audit | ConvertTo-Json -Compress -Depth 10
Add-Content -LiteralPath $auditPath -Value $auditLine -Encoding UTF8

if (-not $remoteVerified) {
    throw "Catalogue commit was pushed, but the distribution endpoint did not verify within the bounded retry window. Do not announce publication success yet."
}

Write-Output "WRN_CATALOGUE_PUBLISH_SUCCESS"
Write-Output ("Release: " + $nextRelease)
Write-Output ("Commit: " + $commitSha)
Write-Output ("Remote SHA-256: " + $candidateHash)
Write-Output ("Audit: " + $auditPath)
