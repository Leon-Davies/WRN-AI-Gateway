param(
    [Parameter(Mandatory = $true)]
    [string]$KeyEnvFile,
    [int]$Port = 18937,
    [string]$ModelKey
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

function Test-FriendlyFailureBody {
    param(
        [string]$Body,
        [string]$UpstreamModel
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return $false
    }

    if ($Body.Contains("openrouter.ai") -or
        (-not [string]::IsNullOrWhiteSpace($UpstreamModel) -and
         $Body.Contains($UpstreamModel))) {
        return $false
    }

    return (
        $Body.Contains("model service") -or
        $Body.Contains("OpenRouter connection needs attention") -or
        $Body.Contains("usage limit") -or
        $Body.Contains("WRN Claude")
    )
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
    $selectedKey = if ([string]::IsNullOrWhiteSpace($ModelKey)) {
        $catalogue.defaultModelKey
    } else {
        $ModelKey
    }
    $model = @($catalogue.models | Where-Object {
        $_.key -eq $selectedKey -and $_.visible
    })[0]
    if (-not $model) {
        throw ("Visible catalogue model not found: " + $selectedKey)
    }
    $headers = @{
        Authorization = ("Bearer " + $localKey)
        "anthropic-version" = "2023-06-01"
    }

    $body = @{
        model = $model.claudeAlias
        max_tokens = 1200
        messages = @(@{ role = "user"; content = "Reply with exactly GATEWAY_DIRECT_OK and nothing else." })
    } | ConvertTo-Json -Depth 12

    $successObserved = $false
    $nonStreamOutcome = $null
    $response = $null

    try {
        $response = Invoke-RestMethod -Uri ("http://127.0.0.1:" + $Port + "/v1/messages") -Headers $headers -Method Post -ContentType "application/json" -Body $body -TimeoutSec 120
        $text = (($response.content | Where-Object { $_.type -eq "text" } | ForEach-Object { $_.text }) -join "").Trim()

        if ($text -ne "GATEWAY_DIRECT_OK") {
            throw "Unexpected non-stream gateway response."
        }

        $successObserved = $true
        $nonStreamOutcome = "SUCCESS"
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        $friendlyBody = [string]$_.ErrorDetails.Message

        if ($status -in @(400, 401, 402, 429, 503) -and
            (Test-FriendlyFailureBody -Body $friendlyBody -UpstreamModel $model.upstreamModel)) {
            $nonStreamOutcome = "FRIENDLY_FAILURE_" + $status
        }
        else {
            throw
        }
    }

    $streamBody = @{
        model = $model.claudeAlias
        max_tokens = 1200
        stream = $true
        messages = @(@{ role = "user"; content = "Reply with exactly GATEWAY_STREAM_OK and nothing else." })
    } | ConvertTo-Json -Depth 12

    $streamOutcome = $null
    $streamResponse = $null

    try {
        $streamResponse = Invoke-WebRequest -UseBasicParsing -Uri ("http://127.0.0.1:" + $Port + "/v1/messages") -Headers $headers -Method Post -ContentType "application/json" -Body $streamBody -TimeoutSec 120
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        $friendlyBody = [string]$_.ErrorDetails.Message

        if ($status -in @(400, 401, 402, 429, 503) -and
            (Test-FriendlyFailureBody -Body $friendlyBody -UpstreamModel $model.upstreamModel)) {
            $streamOutcome = "FRIENDLY_FAILURE_" + $status
        }
        else {
            throw
        }
    }

    if ($streamResponse) {
        $parts = New-Object System.Collections.Generic.List[string]
        $errorMessages = New-Object System.Collections.Generic.List[string]

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

            if ($event.type -eq "error" -and $event.error.message) {
                $errorMessages.Add([string]$event.error.message)
            }
        }

        if (($parts -join "").Trim() -eq "GATEWAY_STREAM_OK" -and
            ([string]$streamResponse.Content).Contains("message_stop")) {
            $successObserved = $true
            $streamOutcome = "SUCCESS"
        }
        elseif ($errorMessages.Count -gt 0) {
            foreach ($message in $errorMessages) {
                if (-not (Test-FriendlyFailureBody -Body $message -UpstreamModel $model.upstreamModel)) {
                    throw "Streamed upstream error was not sanitized."
                }
            }

            $streamOutcome = "FRIENDLY_FAILURE_EVENT"
        }
        else {
            throw "Unexpected streamed gateway response."
        }
    }

    if (-not $successObserved) {
        throw "Live gateway smoke observed no successful inference."
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

    $provider = if ($response -and $response.provider) { [string]$response.provider } else { "<not-observed>" }

    $gatewayLog = Join-Path $state "gateway\gateway.log"
    if (Test-Path -LiteralPath $gatewayLog) {
        $logText = Get-Content -LiteralPath $gatewayLog -Raw
        if ($logText.Contains("GATEWAY_DIRECT_OK") -or
            $logText.Contains("GATEWAY_STREAM_OK") -or
            $logText.Contains("openrouter.ai/settings")) {
            throw "Gateway log contains request/upstream error content."
        }
    }

    Write-Output ("HEALTH=PASS release=" + $health.catalogueRelease)
    Write-Output ("NONSTREAM=" + $nonStreamOutcome + " alias=" + $model.claudeAlias + " provider=" + $provider)
    Write-Output ("STREAM=" + $streamOutcome)
    Write-Output "FRIENDLY_FAILURE_SANITIZATION=PASS"
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
