param(
    [string]$Version = "0.1.0-dev",
    [int]$AppRelease = 0,
    [string]$BrandAssetDirectory = "",
    [switch]$RequireBranding,
    [string]$CodeSigningThumbprint = "",
    [string]$TimestampServer = "",
    [switch]$RequireCodeSigning
)
$ErrorActionPreference = "Stop"

$codeSigningRequested =
    $RequireCodeSigning -or
    -not [string]::IsNullOrWhiteSpace($CodeSigningThumbprint)

function Resolve-WRNCodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )

    $normalized = ($Thumbprint -replace "\s", "").ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "A code-signing certificate thumbprint is required."
    }

    foreach ($store in @(
        "Cert:\CurrentUser\My",
        "Cert:\LocalMachine\My"
    )) {
        $candidate = @(
            Get-ChildItem $store -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Thumbprint -eq $normalized -and
                $_.HasPrivateKey
            }
        ) | Select-Object -First 1

        if ($candidate) {
            $ekuOids = @(
                $candidate.EnhancedKeyUsageList |
                ForEach-Object { $_.ObjectId.Value }
            )

            if ($ekuOids -notcontains "1.3.6.1.5.5.7.3.3") {
                throw "The requested certificate is not valid for code signing."
            }

            $now = Get-Date
            if ($candidate.NotBefore -gt $now -or
                $candidate.NotAfter -le $now) {
                throw "The requested code-signing certificate is not currently valid."
            }

            return $candidate
        }
    }

    throw "The requested code-signing certificate with private key was not found."
}

function Sign-WRNExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory = $true)]
        [string]$TimestampUrl
    )

    if (-not (Test-Path $Path)) {
        throw "Code-signing target is missing: $Path"
    }

    $result = Set-AuthenticodeSignature -FilePath $Path -Certificate $Certificate -HashAlgorithm SHA256 -TimestampServer $TimestampUrl
    if ($result.Status -ne "Valid") {
        throw ("Authenticode signing failed for {0}: {1}" -f $Path, $result.Status)
    }

    $verified = Get-AuthenticodeSignature -FilePath $Path
    if ($verified.Status -ne "Valid" -or
        $verified.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) {
        throw "Authenticode verification failed after signing: $Path"
    }
}

$signingCertificate = $null
if ($codeSigningRequested) {
    if ([string]::IsNullOrWhiteSpace($CodeSigningThumbprint)) {
        throw "A code-signing certificate thumbprint is required when code signing is enabled."
    }

    if ([string]::IsNullOrWhiteSpace($TimestampServer)) {
        throw "A timestamp server is required when code signing is enabled."
    }

    $signingCertificate = Resolve-WRNCodeSigningCertificate -Thumbprint $CodeSigningThumbprint
}

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$releaseRoot = Join-Path $root "release"
$release = Join-Path $releaseRoot ("WRN-AI-Gateway-v" + $Version)
$appDir = Join-Path $release "app"

if ([string]::IsNullOrWhiteSpace($BrandAssetDirectory)) {
    & (Join-Path $PSScriptRoot "build.ps1")
} else {
    & (Join-Path $PSScriptRoot "build.ps1") -BrandAssetDirectory $BrandAssetDirectory
}

if ($RequireBranding) {
    $heroes = @(Get-ChildItem (Join-Path $dist "assets") -Filter "wrn-hero*.png" -File -ErrorAction SilentlyContinue)
    if ($heroes.Count -eq 0) {
        throw "Required WRN hero images were not included in the build."
    }
}

if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Copy-Item (Join-Path $dist "*") $appDir -Recurse -Force

$identity = [ordered]@{
    schemaVersion = 1
    release = $AppRelease
    version = $Version
}
$identityJson = $identity | ConvertTo-Json
[IO.File]::WriteAllText(
    (Join-Path $appDir "app-release.json"),
    ($identityJson + [Environment]::NewLine),
    (New-Object Text.UTF8Encoding($false)))

if ($codeSigningRequested) {
    foreach ($targetName in @(
        "WRN-AI-Gateway.exe",
        "WRN-AI-Gateway-Gateway.exe",
        "WRN-AI-Gateway-Updater.exe"
    )) {
        Sign-WRNExecutable -Path (Join-Path $appDir $targetName) -Certificate $signingCertificate -TimestampUrl $TimestampServer
    }
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$setupSource = Join-Path $root "src\WRN.AIGateway.Setup\Setup.cs"
$setupExe = Join-Path $release "WRN-AI-Gateway-Setup.exe"
$payloadZip = Join-Path $releaseRoot ("WRN-AI-Gateway-v" + $Version + "-payload.zip")
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $payloadZip -CompressionLevel Optimal
$icon = Join-Path $root "src\WRN.AIGateway\assets\wrn-ai-gateway.ico"

$compileArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$setupExe",
    "/reference:$framework\System.dll",
    "/reference:$framework\System.Core.dll",
    "/reference:$framework\Microsoft.CSharp.dll",
    "/reference:$framework\System.Drawing.dll",
    "/reference:$framework\System.Windows.Forms.dll",
    "/reference:$framework\System.IO.Compression.dll",
    "/reference:$framework\System.IO.Compression.FileSystem.dll",
    "/resource:$payloadZip,WRN.AIGateway.Payload.zip"
)
if (Test-Path $icon) {
    $compileArgs += "/win32icon:$icon"
}
$compileArgs += $setupSource
& $csc $compileArgs
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed with exit code $LASTEXITCODE" }
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

if ($codeSigningRequested) {
    Sign-WRNExecutable -Path $setupExe -Certificate $signingCertificate -TimestampUrl $TimestampServer
}

if ($RequireCodeSigning -and -not $codeSigningRequested) {
    throw "Required Authenticode signing was not performed."
}

$readme = @(
    "WRN AI Gateway",
    "",
    "1. Double-click WRN-AI-Gateway-Setup.exe",
    "2. Click Install",
    "3. Open WRN AI Gateway."
)
$readme | Set-Content (Join-Path $release "README-FIRST.txt") -Encoding UTF8
$Version | Set-Content (Join-Path $release "VERSION.txt") -Encoding ASCII

$hashTargets = @(
    (Join-Path $release "WRN-AI-Gateway-Setup.exe"),
    (Join-Path $appDir "WRN-AI-Gateway.exe"),
    (Join-Path $appDir "WRN-AI-Gateway-Gateway.exe"),
    (Join-Path $appDir "WRN-AI-Gateway-Updater.exe"),
    (Join-Path $appDir "app-release.json"),
    (Join-Path $appDir "ui\MainWindow.xaml"),
    (Join-Path $appDir "catalogue\catalogue.json"),
    (Join-Path $appDir "catalogue\catalogue.sig")
)
$hashTargets += @(
    Get-ChildItem (Join-Path $appDir "assets") -Filter "wrn-hero*.png" -File -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName
)
$hashLines = foreach ($target in $hashTargets) {
    $hash = Get-FileHash $target -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), ($target.Substring($release.Length + 1))
}
$hashLines | Set-Content (Join-Path $release "SHA256SUMS.txt") -Encoding ASCII

$zip = $release + ".zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $release "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Release package ready:" -ForegroundColor Green
Write-Host "  $release"
Write-Host "  $zip"
