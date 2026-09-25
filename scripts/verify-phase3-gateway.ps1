$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$shared = Join-Path $root "src\WRN.AIGateway"
$gateway = Join-Path $root "src\WRN.AIGateway.Gateway"
$temp = Join-Path $env:TEMP ("wrn-phase3-gateway-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $gatewayExe = Join-Path $dist "WRN-AI-Gateway-Gateway.exe"
    if (-not (Test-Path -LiteralPath $gatewayExe)) {
        throw "Gateway binary was not built."
    }

    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $testExe = Join-Path $temp "GatewayPolicyTests.exe"

    $compileArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$testExe",
        ("/reference:" + (Join-Path $framework "System.dll")),
        ("/reference:" + (Join-Path $framework "System.Core.dll")),
        ("/reference:" + (Join-Path $framework "System.Web.Extensions.dll")),
        (Join-Path $shared "ModelCatalogue.cs"),
        (Join-Path $gateway "GatewayPolicy.cs"),
        (Join-Path $root "tests\GatewayPolicyTests.cs")
    )

    & $csc $compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Gateway policy test compilation failed."
    }

    & $testExe (Join-Path $dist "catalogue\catalogue.json") (Join-Path $dist "catalogue\catalogue.sig")
    if ($LASTEXITCODE -ne 0) {
        throw "Gateway policy tests failed."
    }

    $catalogue = Get-Content (Join-Path $dist "catalogue\catalogue.json") -Raw | ConvertFrom-Json
    $default = @($catalogue.models | Where-Object { $_.key -eq $catalogue.defaultModelKey })[0]
    $requestPath = Join-Path $temp "request.json"
    $reportPath = Join-Path $temp "policy-report.json"

    $request = [PSCustomObject][ordered]@{
        model = $default.claudeAlias
        max_tokens = 32
        provider = [PSCustomObject][ordered]@{
            zdr = $false
            data_collection = "allow"
        }
        messages = @()
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText(
        $requestPath,
        (($request | ConvertTo-Json -Depth 20) + [Environment]::NewLine),
        $encoding)

    $proc = Start-Process -FilePath $gatewayExe -ArgumentList @("--gateway-policy-report", $requestPath, $reportPath) -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "Gateway policy-report command failed."
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.allowed -ne $true) { throw "Policy report rejected the signed default alias." }
    if ($report.upstreamModel -ne $default.upstreamModel) { throw "Policy report did not use catalogue routing." }

    $outbound = $report.outboundJson | ConvertFrom-Json
    if ($outbound.provider.zdr -ne $true) { throw "Policy report did not force ZDR." }
    if ($outbound.provider.data_collection -ne "deny") { throw "Policy report did not deny data collection." }

    $gatewaySource = (Get-Content (Join-Path $gateway "GatewayPolicy.cs") -Raw) +
        (Get-Content (Join-Path $gateway "Program.cs") -Raw)

    foreach ($forbiddenModel in @(
        "openai/gpt-",
        "deepseek/deepseek-",
        "anthropic/claude-opus",
        "GPT-6 Luna",
        "DeepSeek V4.1"
    )) {
        if ($gatewaySource.Contains($forbiddenModel)) {
            throw "Gateway contains hard-coded model-specific routing: $forbiddenModel"
        }
    }

    foreach ($forbiddenClaudeWrite in @(
        "Claude-3p",
        "configLibrary",
        "deploymentMode",
        "Stop-Process",
        "CurrentVersion\Run"
    )) {
        if ($gatewaySource.Contains($forbiddenClaudeWrite)) {
            throw "Gateway contains Claude lifecycle/config mutation logic: $forbiddenClaudeWrite"
        }
    }

    Write-Host "PASS: gateway builds"
    Write-Host "PASS: signed catalogue aliases drive routing"
    Write-Host "PASS: direct/unknown upstream IDs rejected"
    Write-Host "PASS: ZDR and data-collection policy forced"
    Write-Host "PASS: gateway contains no Claude lifecycle/config writes"
    Write-Host "PHASE3_GATEWAY_VERIFY_PASS" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
