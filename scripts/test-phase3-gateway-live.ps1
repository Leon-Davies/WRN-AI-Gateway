param(
    [Parameter(Mandatory = $true)]
    [string]$KeyEnvFile,
    [int]$Port = 18937
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security

$root = Split-Path -Parent $PSScriptRoot
$gateway = Join-Path $root "dist\WRN-AI-Gateway-Gateway.exe"
$state = Join-Path $env:TEMP ("wrn-phase3-live-" + [Guid]::NewGuid().ToString("N"))
$process = $null

function Get-OpenRouterKey {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "OpenRouter key file not found."
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*OPENROUTER_API_KEY(?:1)?\s*=\s*(.+?)\s*$') {
            return $matches[1].Trim().Trim('"').Trim("'")
        }
    }

    throw "OpenRouter key entry not found."
}

try {
    & (Join-Path $PSScriptRoot "build-gateway.ps1") | Out-Null

    New-Item -ItemType Directory -Force -Path (Join-Path $state "gateway") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $state "credentials") | Out-Null

    $openRouterKey = Get-OpenRouterKey $KeyEnvFile
    $plain = [Text.Encoding]::UTF8.GetBytes($openRouterKey)
    try {
        $protected = [System.Security.Cryptography.ProtectedData]::Protect(
            $plain,
            $null,
            [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        try {
            [IO.File]::WriteAllText(
                (Join-Path $state "credentials\openrouter.key.dpapi"),
                [Convert]::ToBase64String($protected),
                [Text.Encoding]::ASCII)
        }
        finally {
            [Array]::Clear($protected, 0, $protected.Length)
        }
    }
    finally {
        [Array]::Clear($plain, 0, $plain.Length)
    }
    $openRouterKey = $null

    $localKey = [Guid]::NewGuid().ToString("N") + [Guid]::NewGuid().ToString("N")
    $config = @{ LocalApiKey = $localKey; Port = $Port } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText(
        (Join-Path $state "gateway\gateway.json"),
        $config,
        (New-Object Text.UTF8Encoding($false)))

    $process = Start-Process -FilePath $gateway -ArgumentList @("--state-root", $state) -PassThru

    $health = $null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        try {
            $health = Invoke-RestMethod -Uri ("http://127.0.0.1:" + $Port + "/health") -TimeoutSec 2
            if ($health.ok -eq $true) { break }
        }
        catch {
        }
    }

    if (-not $health -or $health.ok -ne $true) {
        throw "Gateway health check failed."
    }

    $cataloguePath = Join-Path $env:LOCALAPPDATA (
        "WRN-AI-Gateway\catalogue\releases\" +
        ([int]$health.catalogueRelease).ToString("D8") +
        "\catalogue.json")
    if (-not (Test-Path -LiteralPath $cataloguePath)) {
        $cataloguePath = Join-Path $root "dist\catalogue\catalogue.json"
    }

    $catalogue = Get-Content -LiteralPath $cataloguePath -Raw | ConvertFrom-Json
    $model = @($catalogue.models | Where-Object { $_.key -eq $catalogue.defaultModelKey })[0]
    $headers = @{
        Authorization = ("Bearer " + $localKey)
        "anthropic-version" = "2023-06-01"
    }

    $body = @{
        model = $model.claudeAlias
        max_tokens = 1200
        messages = @(@{ role = "user"; content = "Reply with exactly GATEWAY_DIRECT_OK and nothing else." })
    } | ConvertTo-Json -Depth 12

    $response = Invoke-RestMethod -Uri ("http://127.0.0.1:" + $Port + "/v1/messages") -Headers $headers -Method Post -ContentType "application/json" -Body $body -TimeoutSec 120
    $text = (($response.content | Where-Object { $_.type -eq "text" } | ForEach-Object { $_.text }) -join "").Trim()
    if ($text -ne "GATEWAY_DIRECT_OK") {
        throw "Unexpected non-stream gateway response."
    }

    $streamBody = @{
        model = $model.claudeAlias
        max_tokens = 1200
        stream = $true
        messages = @(@{ role = "user"; content = "Reply with exactly GATEWAY_STREAM_OK and nothing else." })
    } | ConvertTo-Json -Depth 12

    $streamResponse = Invoke-WebRequest -UseBasicParsing -Uri ("http://127.0.0.1:" + $Port + "/v1/messages") -Headers $headers -Method Post -ContentType "application/json" -Body $streamBody -TimeoutSec 120

    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($line in ([string]$streamResponse.Content -split "[\r\n]+")) {
        if (-not $line.StartsWith("data: ")) { continue }
        $payload = $line.Substring(6).Trim()
        if (-not $payload -or $payload -eq "[DONE]") { continue }

        try { $event = $payload | ConvertFrom-Json } catch { continue }

        if ($event.type -eq "content_block_delta" -and
            $event.delta.type -eq "text_delta" -and
            $event.delta.text) {
            $parts.Add([string]$event.delta.text)
        }
    }

    if (($parts -join "").Trim() -ne "GATEWAY_STREAM_OK" -or
        -not ([string]$streamResponse.Content).Contains("message_stop")) {
        throw "Unexpected streamed gateway response."
    }

    try {
        Invoke-WebRequest -UseBasicParsing -Uri ("http://127.0.0.1:" + $Port + "/v1/messages") -Headers @{ Authorization = "Bearer wrong"; "anthropic-version" = "2023-06-01" } -Method Post -ContentType "application/json" -Body $body -TimeoutSec 10 | Out-Null
        throw "Bad local authentication was unexpectedly accepted."
    }
    catch {
        if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 401) {
            throw
        }
    }

    $direct = @{ model = $model.upstreamModel; max_tokens = 64; messages = @() } | ConvertTo-Json -Depth 8

    try {
        Invoke-WebRequest -UseBasicParsing -Uri ("http://127.0.0.1:" + $Port + "/v1/messages") -Headers $headers -Method Post -ContentType "application/json" -Body $direct -TimeoutSec 10 | Out-Null
        throw "Direct upstream model ID was unexpectedly accepted."
    }
    catch {
        if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 400) {
            throw
        }
    }

    Write-Output ("HEALTH=PASS release=" + $health.catalogueRelease)
    Write-Output ("NONSTREAM=PASS alias=" + $model.claudeAlias + " provider=" + $response.provider)
    Write-Output "STREAM=PASS"
    Write-Output "BAD_AUTH=PASS"
    Write-Output "DIRECT_UPSTREAM_ID_REJECTED=PASS"
    Write-Output "PHASE3_LIVE_GATEWAY_SMOKE_PASS"
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $state) {
        Remove-Item -LiteralPath $state -Recurse -Force
    }
}
