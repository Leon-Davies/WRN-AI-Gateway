param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "SilentlyContinue"

function Get-FileMetadata {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            exists = $false
            size = 0
            sha256 = $null
        }
    }

    $item = Get-Item -LiteralPath $Path
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256

    return [ordered]@{
        exists = $true
        size = [long]$item.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    }
}

$local = [Environment]::GetFolderPath("LocalApplicationData")
$thirdParty = Join-Path $local "Claude-3p"
$configLibrary = Join-Path $thirdParty "configLibrary"

$desktopConfig = Join-Path $thirdParty "claude_desktop_config.json"
$meta = Join-Path $configLibrary "_meta.json"
$wrnProfile = Join-Path $configLibrary "a179a3b8-7f6e-4c80-9e33-3ed210fe3d41.json"

$currentPackages = @(
    Get-AppxPackage "*Claude*" |
        Select-Object Name,PackageFullName,Version,PublisherId,InstallLocation,Status
)

$service = Get-CimInstance Win32_Service -Filter "Name='CoworkVMService'"

$processes = @(
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -match "^claude(\.exe)?$|cowork" } |
        Select-Object ProcessId,ParentProcessId,Name,ExecutablePath
)

$startApps = @(
    Get-StartApps |
        Where-Object { $_.Name -match "Claude" } |
        Select-Object Name,AppID
)

$document = [ordered]@{
    schemaVersion = 1
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    readOnly = $true
    computerName = $env:COMPUTERNAME

    package = [ordered]@{
        currentUser = $currentPackages
        startApps = $startApps
    }

    workspaceService = if ($service) {
        [ordered]@{
            present = $true
            state = [string]$service.State
            startMode = [string]$service.StartMode
            startName = [string]$service.StartName
            processId = [int]$service.ProcessId
            pathName = [string]$service.PathName
        }
    } else {
        [ordered]@{
            present = $false
            state = $null
            startMode = $null
            startName = $null
            processId = 0
            pathName = $null
        }
    }

    claudeProcesses = $processes

    wrnWriteAllowlist = [ordered]@{
        desktopConfig = Get-FileMetadata $desktopConfig
        meta = Get-FileMetadata $meta
        wrnProfile = Get-FileMetadata $wrnProfile
    }

    privacy = "No Claude configuration contents, chat/history, cookies, browser/session stores, prompt text, user files, or credentials are captured."
}

$parent = Split-Path -Parent $OutputPath
if ($parent) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText(
    $OutputPath,
    ($document | ConvertTo-Json -Depth 12),
    $utf8)

Write-Output "WRN_MANAGED_CLAUDE_BASELINE_CAPTURE_PASS"
Write-Output ("OUTPUT=" + $OutputPath)
