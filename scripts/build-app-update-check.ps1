$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$tool = Join-Path $root "tools\WRN.AppUpdateCheck"
$outDir = Join-Path $root "dist\maintainer"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $outDir "WRN-AppUpdateCheck.exe"

$args = @(
    "/nologo",
    "/target:exe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$out",
    ("/reference:" + (Join-Path $framework "System.dll")),
    ("/reference:" + (Join-Path $framework "System.Core.dll")),
    ("/reference:" + (Join-Path $framework "System.Security.dll")),
    ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
    ("/reference:" + (Join-Path $framework "System.Xml.dll")),
    ("/reference:" + (Join-Path $framework "System.IO.Compression.dll")),
    ("/reference:" + (Join-Path $framework "System.IO.Compression.FileSystem.dll")),
    (Join-Path $src "ModelCatalogue.cs"),
    (Join-Path $src "AppUpdate.cs"),
    (Join-Path $tool "Program.cs")
)

& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw "App update check tool compilation failed."
}

Write-Output $out
