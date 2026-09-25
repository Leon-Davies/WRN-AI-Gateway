$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$gatewaySrc = Join-Path $root "src\WRN.AIGateway.Gateway"
$sharedSrc = Join-Path $root "src\WRN.AIGateway"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $dist "WRN-AI-Gateway-Gateway.exe"

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$out",
    ("/reference:" + (Join-Path $framework "System.dll")),
    ("/reference:" + (Join-Path $framework "System.Core.dll")),
    ("/reference:" + (Join-Path $framework "System.Net.Http.dll")),
    ("/reference:" + (Join-Path $framework "System.Security.dll")),
    ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
    (Join-Path $sharedSrc "ModelCatalogue.cs"),
    (Join-Path $sharedSrc "RuntimeFailures.cs"),
    (Join-Path $gatewaySrc "GatewayPolicy.cs"),
    (Join-Path $gatewaySrc "Program.cs")
)

& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw "Gateway compilation failed."
}

Write-Output $out
