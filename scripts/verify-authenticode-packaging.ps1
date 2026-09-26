param()
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $PSScriptRoot "package.ps1"
$source = Get-Content $package -Raw

$tokens = $null
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $package,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw ("package.ps1 has parse errors: " + ($parseErrors | ForEach-Object { $_.Message } | Out-String))
}

foreach ($required in @(
    "CodeSigningThumbprint",
    "TimestampServer",
    "RequireCodeSigning",
    "Resolve-WRNCodeSigningCertificate",
    "Set-AuthenticodeSignature",
    "Get-AuthenticodeSignature",
    "1.3.6.1.5.5.7.3.3",
    "WRN-AI-Gateway.exe",
    "WRN-AI-Gateway-Gateway.exe",
    "WRN-AI-Gateway-Updater.exe"
)) {
    if (-not $source.Contains($required)) {
        throw "Required Authenticode packaging primitive missing: $required"
    }
}

$payloadSign = $source.IndexOf('Sign-WRNExecutable -Path (Join-Path $appDir $targetName)')
$payloadZip = $source.IndexOf('Compress-Archive -Path (Join-Path $appDir "*")')
$setupCompile = $source.IndexOf('& $csc $compileArgs')
$setupSign = $source.IndexOf('Sign-WRNExecutable -Path $setupExe')
$hashes = $source.IndexOf('$hashTargets = @(')

if ($payloadSign -lt 0 -or $payloadZip -lt 0 -or $payloadSign -gt $payloadZip) {
    throw "Payload executables must be signed before the embedded payload ZIP is created."
}
if ($setupCompile -lt 0 -or $setupSign -lt 0 -or $setupSign -lt $setupCompile) {
    throw "Setup must be signed after Setup compilation."
}
if ($hashes -lt 0 -or $setupSign -gt $hashes) {
    throw "Release hashes must be generated after Authenticode verification."
}

$negativeVersion = "0.0.0-authenticode-negative-verify"
$negativeRelease = Join-Path $root ("release\WRN-AI-Gateway-v" + $negativeVersion)
$negativeZip = $negativeRelease + ".zip"
foreach ($path in @($negativeRelease, $negativeZip)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

$failedAsExpected = $false
try {
    & $package `
        -Version $negativeVersion `
        -AppRelease 999998 `
        -CodeSigningThumbprint "0000000000000000000000000000000000000000" `
        -TimestampServer "https://timestamp.invalid.example" `
        -RequireCodeSigning
}
catch {
    if ($_.Exception.Message -match "code-signing certificate.*not found") {
        $failedAsExpected = $true
    }
    else {
        throw
    }
}

if (-not $failedAsExpected) {
    throw "Trusted-release packaging did not fail closed when the signing identity was unavailable."
}
if (Test-Path $negativeRelease -or Test-Path $negativeZip) {
    throw "Trusted-release signing preflight created release output before certificate validation."
}

Write-Host "PASS: package.ps1 parses with optional Authenticode parameters" -ForegroundColor Green
Write-Host "PASS: payload EXEs are signed before embedding; Setup is signed after compilation" -ForegroundColor Green
Write-Host "PASS: hashes are generated only after Authenticode verification" -ForegroundColor Green
Write-Host "PASS: trusted-release mode fails before release output when signing identity is unavailable" -ForegroundColor Green
Write-Host "AUTHENTICODE_PACKAGING_VERIFY_PASS" -ForegroundColor Green
