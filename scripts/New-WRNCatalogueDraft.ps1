param(
    [string]$Destination
)

$ErrorActionPreference = "Stop"

$repoUrl = "https://github.com/Leon-Davies/WRN-AI-Gateway.git"
$branch = "catalogue-beta"
$maintainerRoot = Join-Path $env:LOCALAPPDATA "WRN-AI-Gateway-Maintainer"
$publisherRepo = Join-Path $maintainerRoot "catalogue-repo"
$draftRoot = Join-Path $maintainerRoot "drafts"

function Invoke-Git {
    param([string[]]$Arguments)
    & wsl.exe -- git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed."
    }
}

New-Item -ItemType Directory -Force -Path $maintainerRoot | Out-Null
New-Item -ItemType Directory -Force -Path $draftRoot | Out-Null
if ($publisherRepo -notmatch '^([A-Za-z]):\\(.*)$') {
    throw "Publisher workspace must be on a Windows drive that WSL can mount."
}
$publisherRepoGit = "/mnt/" + $matches[1].ToLowerInvariant() + "/" + ($matches[2] -replace '\\', '/')

if (-not (Test-Path -LiteralPath (Join-Path $publisherRepo ".git"))) {
    Invoke-Git @("clone", "--branch", $branch, "--single-branch", $repoUrl, $publisherRepoGit)
}

Invoke-Git @("-C", $publisherRepoGit, "config", "core.autocrlf", "false")
$dirty = & wsl.exe -- git -C $publisherRepoGit status --porcelain
if ($LASTEXITCODE -ne 0) { throw "Could not inspect publisher workspace." }
if ($dirty) {
    throw "Publisher workspace contains local changes. Resolve them before preparing another draft."
}

Invoke-Git @("-C", $publisherRepoGit, "fetch", "origin", $branch)
Invoke-Git @("-C", $publisherRepoGit, "checkout", $branch)
Invoke-Git @("-C", $publisherRepoGit, "reset", "--hard", "origin/$branch")

$source = Join-Path $publisherRepo "catalogue\catalogue.json"
if (-not (Test-Path -LiteralPath $source)) {
    throw "Remote catalogue file is missing from the publisher workspace."
}

$current = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
if (-not $Destination) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $Destination = Join-Path $draftRoot ("catalogue-draft-" + $stamp + ".json")
}

Copy-Item -LiteralPath $source -Destination $Destination -Force

Write-Output "WRN_CATALOGUE_DRAFT_READY"
Write-Output ("Current release: " + $current.release)
Write-Output ("Draft: " + $Destination)
Write-Output "Edit only the draft. The publisher assigns the next release number and publication timestamp."
