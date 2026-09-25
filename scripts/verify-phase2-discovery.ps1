$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\WRN.AIGateway"
$testSource = Join-Path $root "tests\ClaudeDiscoveryTests.cs"
$dist = Join-Path $root "dist"
$temp = Join-Path $env:TEMP ("wrn-phase2-discovery-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "ClaudeDiscoveryTests.exe"
    $compileArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        (Join-Path $src "ClaudeDiscovery.cs"),
        $testSource
    )
    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) { throw "Discovery test compilation failed." }

    & $testExe
    if ($LASTEXITCODE -ne 0) { throw "Discovery fixture tests failed." }

    $report = Join-Path $temp "discovery.json"
    $proc = Start-Process -FilePath (Join-Path $dist "WRN-AI-Gateway.exe") -ArgumentList @("--discovery-report", $report) -Wait -PassThru
    if ($proc.ExitCode -ne 0) { throw "Discovery command exited with code $($proc.ExitCode)." }
    if (-not (Test-Path -LiteralPath $report)) { throw "Discovery report was not created." }

    $json = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($json.Discovery.ReadOnly -ne $true) { throw "Discovery report is not marked read-only." }
    if (-not $json.Discovery.InstallKindName) { throw "Discovery report did not classify installation state." }
    if (-not $json.Discovery.ModeName) { throw "Discovery report did not classify Claude mode." }

    $raw = Get-Content -LiteralPath $report -Raw
    foreach ($forbidden in @("openai/gpt-", "deepseek/", "anthropic/claude-opus")) {
        if ($raw -match [regex]::Escape($forbidden)) {
            throw "Discovery report unexpectedly contains a model-specific identifier: $forbidden"
        }
    }

    $discoverySource = Get-Content (Join-Path $src "ClaudeDiscovery.cs") -Raw
    foreach ($forbiddenApi in @("File.Delete(", "File.Move(", "File.Copy(", "Directory.Delete(", "Registry.SetValue(", "SetValue(", "Process.Start(")) {
        if ($discoverySource.Contains($forbiddenApi)) {
            throw "Read-only discovery source contains forbidden mutation API: $forbiddenApi"
        }
    }

    Write-Host ""
    Write-Host "Current machine discovery:" -ForegroundColor Cyan
    Write-Host ("  InstallKind: " + $json.Discovery.InstallKindName)
    Write-Host ("  Mode: " + $json.Discovery.ModeName)
    Write-Host ("  ClaudeRunning: " + $json.Discovery.ClaudeRunning)
    Write-Host ("  PlanToWrnAllowed: " + $json.PlanToWrn.Allowed)
    if ($json.PlanToWrn.BlockReason) {
        Write-Host ("  PlanToWrnBlock: " + $json.PlanToWrn.BlockReason)
    }

    Write-Host ""
    Write-Host "PHASE2_DISCOVERY_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
