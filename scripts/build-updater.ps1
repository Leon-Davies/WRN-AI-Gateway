$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway.Updater"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $dist "WRN-AI-Gateway-Updater.exe"
$icon = Join-Path $root "src\WRN.AIGateway\assets\wrn-ai-gateway.ico"

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$out",
    ("/reference:" + (Join-Path $framework "System.dll")),
    ("/reference:" + (Join-Path $framework "System.Core.dll")),
    ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll"))
)

if (Test-Path -LiteralPath $icon) {
    $args += "/win32icon:$icon"
}

$args += (Join-Path $src "UpdateActivation.cs")
$args += (Join-Path $src "Program.cs")

& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw "Updater compilation failed."
}

Write-Output $out
