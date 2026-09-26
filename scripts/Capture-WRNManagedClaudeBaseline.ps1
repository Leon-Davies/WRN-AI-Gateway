param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "SilentlyContinue"

function Convert-ToSafePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    $safe = $Path
    $replacements = @(
        @{ value = [Environment]::GetFolderPath("LocalApplicationData"); token = "%LOCALAPPDATA%" },
        @{ value = [Environment]::GetFolderPath("ApplicationData"); token = "%APPDATA%" },
        @{ value = [Environment]::GetFolderPath("UserProfile"); token = "%USERPROFILE%" }
    )

    foreach ($replacement in $replacements) {
        if (-not [string]::IsNullOrWhiteSpace([string]$replacement.value)) {
            $safe = $safe -replace [regex]::Escape([string]$replacement.value), [string]$replacement.token
        }
    }

    return $safe
}

function Get-SafeFailureCode {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    $signal = (
        [string]$ErrorRecord.FullyQualifiedErrorId + " " +
        [string]$ErrorRecord.Exception.GetType().FullName + " " +
        [string]$ErrorRecord.Exception.Message
    )

    if ($signal -match "(?i)access|unauthor|elevation|administrator") {
        return "ACCESS_DENIED"
    }

    if ($signal -match "(?i)not recognized|command.*not found") {
        return "COMMAND_UNAVAILABLE"
    }

    return "QUERY_FAILED"
}

function Invoke-SafeQuery {
    param([scriptblock]$Query)

    try {
        $items = @(& $Query)
        return [ordered]@{
            succeeded = $true
            failure = $null
            items = $items
        }
    }
    catch {
        return [ordered]@{
            succeeded = $false
            failure = Get-SafeFailureCode $_
            items = @()
        }
    }
}

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

function Get-SafeServiceAccount {
    param([string]$StartName)

    if ([string]::IsNullOrWhiteSpace($StartName)) {
        return $null
    }

    if (
        $StartName -eq "LocalSystem" -or
        $StartName -eq "LocalService" -or
        $StartName -eq "NetworkService" -or
        $StartName -match "^(?i)NT AUTHORITY\\" -or
        $StartName -match "^(?i)NT SERVICE\\"
    ) {
        return $StartName
    }

    return "NON_BUILTIN_ACCOUNT_REDACTED"
}

function Get-ServiceMetadata {
    param([string]$Name)

    try {
        $service = Get-CimInstance Win32_Service -Filter ("Name='" + $Name + "'") -ErrorAction Stop

        if (-not $service) {
            return [ordered]@{
                querySucceeded = $true
                failure = $null
                present = $false
                state = $null
                startMode = $null
                startAccount = $null
                processId = 0
                pathName = $null
            }
        }

        return [ordered]@{
            querySucceeded = $true
            failure = $null
            present = $true
            state = [string]$service.State
            startMode = [string]$service.StartMode
            startAccount = Get-SafeServiceAccount ([string]$service.StartName)
            processId = [int]$service.ProcessId
            pathName = Convert-ToSafePath ([string]$service.PathName)
        }
    }
    catch {
        return [ordered]@{
            querySucceeded = $false
            failure = Get-SafeFailureCode $_
            present = $null
            state = $null
            startMode = $null
            startAccount = $null
            processId = 0
            pathName = $null
        }
    }
}

function Convert-AppxPackageMetadata {
    param($Package)

    return [ordered]@{
        name = [string]$Package.Name
        packageFullName = [string]$Package.PackageFullName
        version = [string]$Package.Version
        publisherId = [string]$Package.PublisherId
        installLocation = Convert-ToSafePath ([string]$Package.InstallLocation)
        status = [string]$Package.Status
    }
}

$local = [Environment]::GetFolderPath("LocalApplicationData")
$thirdParty = Join-Path $local "Claude-3p"
$configLibrary = Join-Path $thirdParty "configLibrary"

$desktopConfig = Join-Path $thirdParty "claude_desktop_config.json"
$meta = Join-Path $configLibrary "_meta.json"
$wrnProfile = Join-Path $configLibrary "a179a3b8-7f6e-4c80-9e33-3ed210fe3d41.json"

$currentPackageQuery = Invoke-SafeQuery {
    Get-AppxPackage -Name "*Claude*" -ErrorAction Stop |
        ForEach-Object { Convert-AppxPackageMetadata $_ }
}

$allUsersPackageQuery = Invoke-SafeQuery {
    Get-AppxPackage -Name "*Claude*" -AllUsers -ErrorAction Stop |
        ForEach-Object { Convert-AppxPackageMetadata $_ }
}

$provisionedPackageQuery = Invoke-SafeQuery {
    Get-AppxProvisionedPackage -Online -ErrorAction Stop |
        Where-Object {
            $_.DisplayName -match "(?i)Claude" -or
            $_.PackageName -match "(?i)Claude"
        } |
        ForEach-Object {
            [ordered]@{
                displayName = [string]$_.DisplayName
                packageName = [string]$_.PackageName
                version = [string]$_.Version
                architecture = [string]$_.Architecture
            }
        }
}

$startAppsQuery = Invoke-SafeQuery {
    Get-StartApps -ErrorAction Stop |
        Where-Object { $_.Name -match "(?i)Claude" } |
        ForEach-Object {
            [ordered]@{
                name = [string]$_.Name
                appId = [string]$_.AppID
            }
        }
}

$processQuery = Invoke-SafeQuery {
    Get-CimInstance Win32_Process -ErrorAction Stop |
        Where-Object { $_.Name -match "(?i)^claude(\.exe)?$|cowork" } |
        ForEach-Object {
            [ordered]@{
                processId = [int]$_.ProcessId
                parentProcessId = [int]$_.ParentProcessId
                name = [string]$_.Name
                executablePath = Convert-ToSafePath ([string]$_.ExecutablePath)
            }
        }
}

$virtualMachinePlatform = try {
    $feature = Get-WindowsOptionalFeature -Online -FeatureName "VirtualMachinePlatform" -ErrorAction Stop
    [ordered]@{
        querySucceeded = $true
        failure = $null
        featureName = [string]$feature.FeatureName
        state = [string]$feature.State
    }
}
catch {
    [ordered]@{
        querySucceeded = $false
        failure = Get-SafeFailureCode $_
        featureName = "VirtualMachinePlatform"
        state = $null
    }
}

$document = [ordered]@{
    schemaVersion = 2
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    readOnly = $true
    computerName = $env:COMPUTERNAME

    package = [ordered]@{
        currentUser = $currentPackageQuery.items
        allUsers = $allUsersPackageQuery.items
        provisioned = $provisionedPackageQuery.items
        startApps = $startAppsQuery.items
        queryStatus = [ordered]@{
            currentUser = [ordered]@{
                succeeded = $currentPackageQuery.succeeded
                failure = $currentPackageQuery.failure
            }
            allUsers = [ordered]@{
                succeeded = $allUsersPackageQuery.succeeded
                failure = $allUsersPackageQuery.failure
            }
            provisioned = [ordered]@{
                succeeded = $provisionedPackageQuery.succeeded
                failure = $provisionedPackageQuery.failure
            }
            startApps = [ordered]@{
                succeeded = $startAppsQuery.succeeded
                failure = $startAppsQuery.failure
            }
        }
    }

    workspaceService = Get-ServiceMetadata "CoworkVMService"

    virtualization = [ordered]@{
        virtualMachinePlatform = $virtualMachinePlatform
        services = [ordered]@{
            vmcompute = Get-ServiceMetadata "vmcompute"
            hns = Get-ServiceMetadata "hns"
        }
    }

    claudeProcesses = $processQuery.items
    claudeProcessQuery = [ordered]@{
        succeeded = $processQuery.succeeded
        failure = $processQuery.failure
    }

    wrnWriteAllowlist = [ordered]@{
        desktopConfig = Get-FileMetadata $desktopConfig
        meta = Get-FileMetadata $meta
        wrnProfile = Get-FileMetadata $wrnProfile
    }

    privacy = "No Claude configuration contents, chat/history, cookies, browser/session stores, prompt text, user files, WTW usernames, tenant IDs, access tokens, or credentials are captured. User-profile path prefixes are tokenized."
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
