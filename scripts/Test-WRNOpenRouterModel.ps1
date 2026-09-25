param(
    [Parameter(Mandatory = $true)]
    [string]$ModelId,

    [string]$KeyEnvFile,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

function Get-OpenRouterKey {
    if ($env:OPENROUTER_API_KEY) {
        return $env:OPENROUTER_API_KEY.Trim()
    }

    if ($KeyEnvFile) {
        if (-not (Test-Path -LiteralPath $KeyEnvFile)) {
            throw "OpenRouter key file not found."
        }

        foreach ($line in Get-Content -LiteralPath $KeyEnvFile) {
            if ($line -match '^\s*OPENROUTER_API_KEY(?:1)?\s*=\s*(.+?)\s*$') {
                return $matches[1].Trim().Trim('"').Trim("'")
            }
        }
    }

    throw "No OpenRouter key is available. Set OPENROUTER_API_KEY or pass -KeyEnvFile."
}

function Get-TextContent {
    param($Content)
    $parts = @()
    foreach ($item in @($Content)) {
        if ($item.type -eq "text" -and $item.text) {
            $parts += [string]$item.text
        }
    }
    return ($parts -join "").Trim()
}

function Invoke-JsonPost {
    param([string]$Uri, $Body, [hashtable]$Headers, [int]$TimeoutSec = 120)
    $json = $Body | ConvertTo-Json -Depth 30
    return Invoke-RestMethod -Uri $Uri -Method Post -Headers $Headers -ContentType "application/json" -Body $json -TimeoutSec $TimeoutSec
}

$key = Get-OpenRouterKey
if ([string]::IsNullOrWhiteSpace($key)) {
    throw "OpenRouter key is empty."
}

$headers = @{
    Authorization = ("Bearer " + $key)
}
$providerPolicy = @{
    zdr = $true
    data_collection = "deny"
}

$root = Join-Path $env:LOCALAPPDATA "WRN-AI-Gateway-Maintainer\qualifications"
New-Item -ItemType Directory -Force -Path $root | Out-Null

if (-not $OutputPath) {
    $safeName = ($ModelId -replace '[^A-Za-z0-9._-]', '_')
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath = Join-Path $root ($safeName + "-" + $stamp + ".json")
}

$checks = [ordered]@{
    modelExists = $false
    zdrRouteExists = $false
    inference = $false
    streaming = $false
    toolCall = $false
    toolContinuation = $false
}
$providers = @()
$notes = New-Object System.Collections.Generic.List[string]

try {
    $models = Invoke-RestMethod -Uri "https://openrouter.ai/api/v1/models" -Headers $headers -Method Get -TimeoutSec 30
    $checks.modelExists = @($models.data | Where-Object { $_.id -eq $ModelId }).Count -gt 0
}
catch {
    $notes.Add("Model discovery failed: " + $_.Exception.GetType().Name)
}

try {
    $zdr = Invoke-RestMethod -Uri "https://openrouter.ai/api/v1/endpoints/zdr" -Headers $headers -Method Get -TimeoutSec 30
    $matches = @($zdr.data | Where-Object { $_.model_id -eq $ModelId })
    $checks.zdrRouteExists = $matches.Count -gt 0
    $providers = @($matches | ForEach-Object { $_.provider_name } | Where-Object { $_ } | Sort-Object -Unique)
}
catch {
    $notes.Add("ZDR route discovery failed: " + $_.Exception.GetType().Name)
}

if ($checks.modelExists -and $checks.zdrRouteExists) {
    try {
        $response = Invoke-JsonPost -Uri "https://openrouter.ai/api/v1/messages" -Headers $headers -Body @{
            model = $ModelId
            max_tokens = 2500
            provider = $providerPolicy
            messages = @(
                @{
                    role = "user"
                    content = "Reply with exactly WRN_QUAL_OK and nothing else."
                }
            )
        }
        $checks.inference = (Get-TextContent $response.content) -eq "WRN_QUAL_OK"
    }
    catch {
        $notes.Add("Inference check failed: " + $_.Exception.GetType().Name)
    }

    try {
        $streamBody = @{
            model = $ModelId
            max_tokens = 2500
            stream = $true
            provider = $providerPolicy
            messages = @(
                @{
                    role = "user"
                    content = "Reply with exactly WRN_STREAM_OK and nothing else."
                }
            )
        } | ConvertTo-Json -Depth 30

        $stream = Invoke-WebRequest -UseBasicParsing -Uri "https://openrouter.ai/api/v1/messages" -Method Post -Headers $headers -ContentType "application/json" -Body $streamBody -TimeoutSec 120
        $streamText = [string]$stream.Content
        $reconstructed = New-Object System.Text.StringBuilder
        foreach ($line in ($streamText -split "[\r\n]+")) {
            if (-not $line.StartsWith("data: ")) { continue }
            $payload = $line.Substring(6).Trim()
            if (-not $payload -or $payload -eq "[DONE]") { continue }
            try {
                $event = $payload | ConvertFrom-Json
                if ($event.type -eq "content_block_delta" -and $event.delta.type -eq "text_delta" -and $event.delta.text) {
                    [void]$reconstructed.Append([string]$event.delta.text)
                }
            }
            catch {
            }
        }
        $checks.streaming = ($reconstructed.ToString().Trim() -eq "WRN_STREAM_OK") -and $streamText.Contains("message_stop")
    }
    catch {
        $notes.Add("Streaming check failed: " + $_.Exception.GetType().Name)
    }

    $toolPrompt = "You must call the wrn_add tool to add 31 and 11. Do not calculate it yourself. After the tool result is returned, reply with exactly WRN_TOOL_OK."
    $toolDefinition = @{
        name = "wrn_add"
        description = "Add two integers."
        input_schema = @{
            type = "object"
            properties = @{
                a = @{ type = "integer" }
                b = @{ type = "integer" }
            }
            required = @("a", "b")
        }
    }

    try {
        $toolResponse = Invoke-JsonPost -Uri "https://openrouter.ai/api/v1/messages" -Headers $headers -Body @{
            model = $ModelId
            max_tokens = 2500
            provider = $providerPolicy
            tools = @($toolDefinition)
            tool_choice = @{
                type = "tool"
                name = "wrn_add"
            }
            messages = @(
                @{
                    role = "user"
                    content = $toolPrompt
                }
            )
        }

        $toolUse = @($toolResponse.content | Where-Object { $_.type -eq "tool_use" -and $_.name -eq "wrn_add" }) | Select-Object -First 1
        if ($toolUse) {
            $a = [int]$toolUse.input.a
            $b = [int]$toolUse.input.b
            $checks.toolCall = ($a -eq 31 -and $b -eq 11)

            $continued = Invoke-JsonPost -Uri "https://openrouter.ai/api/v1/messages" -Headers $headers -Body @{
                model = $ModelId
                max_tokens = 2500
                provider = $providerPolicy
                tools = @($toolDefinition)
                messages = @(
                    @{
                        role = "user"
                        content = $toolPrompt
                    },
                    @{
                        role = "assistant"
                        content = @($toolResponse.content)
                    },
                    @{
                        role = "user"
                        content = @(
                            @{
                                type = "tool_result"
                                tool_use_id = [string]$toolUse.id
                                content = "42"
                            }
                        )
                    }
                )
            }

            $checks.toolContinuation = (Get-TextContent $continued.content) -eq "WRN_TOOL_OK"
        }
    }
    catch {
        $notes.Add("Tool workflow check failed: " + $_.Exception.GetType().Name)
    }
}

$passed = $true
foreach ($value in $checks.Values) {
    if ($value -ne $true) {
        $passed = $false
    }
}

$report = [PSCustomObject][ordered]@{
    schemaVersion = 1
    modelId = $ModelId
    checkedAt = [DateTimeOffset]::UtcNow.ToString("o")
    passed = $passed
    policy = [PSCustomObject][ordered]@{
        zdrRequired = $true
        dataCollection = "deny"
    }
    zdrProviders = $providers
    checks = [PSCustomObject]$checks
    gatewayMapping = "pending-phase3"
    cowork = "pending-clean-managed-qualification"
    notes = @($notes)
}

$encoding = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText(
    $OutputPath,
    (($report | ConvertTo-Json -Depth 20) + [Environment]::NewLine),
    $encoding)

Write-Output ("Qualification report: " + $OutputPath)
Write-Output ("Model: " + $ModelId)
Write-Output ("ZDR providers: " + ($providers -join ", "))
foreach ($keyName in $checks.Keys) {
    Write-Output ($keyName + "=" + $checks[$keyName])
}
Write-Output ("PASSED=" + $passed)

$key = $null
Remove-Variable key -ErrorAction SilentlyContinue

if (-not $passed) {
    exit 1
}
