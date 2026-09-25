$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$tool = Join-Path $root "tools\WRN.CatalogueCheck"
$out = Join-Path $root "dist\maintainer"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$exe = Join-Path $out "WRN-CatalogueCheck.exe"

$args = @(
    "/nologo",
    "/target:exe",
    "/optimize+",
    "/out:$exe",
    ("/reference:" + (Join-Path $framework "System.dll")),
    ("/reference:" + (Join-Path $framework "System.Core.dll")),
    ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
    (Join-Path $src "ModelCatalogue.cs"),
    (Join-Path $tool "Program.cs")
)

& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw "Catalogue check tool build failed."
}

Write-Output $exe
