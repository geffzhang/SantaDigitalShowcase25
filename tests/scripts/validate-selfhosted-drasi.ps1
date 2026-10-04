[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'http://localhost:8081',
    [string]$DrasiBaseUrl = 'http://localhost:8080',
    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function Assert-HealthEndpoint {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Name,
        [string]$Uri,
        [System.Threading.CancellationToken]$CancellationToken
    )

    $response = $null
    try {
        try {
            $response = $Client.GetAsync($Uri, $CancellationToken).GetAwaiter().GetResult()
        }
        catch {
            throw "$Name health endpoint '$Uri' could not be reached: $($_.Exception.Message)"
        }

        if (-not $response.IsSuccessStatusCode) {
            throw "$Name health endpoint '$Uri' returned HTTP $([int]$response.StatusCode)."
        }
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
    }
}

function Get-DrasiQueryResults {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Uri,
        [System.Threading.CancellationToken]$CancellationToken,
        [switch]$AllowQueryStarting
    )

    $response = $null
    try {
        try {
            $response = $Client.GetAsync($Uri, $CancellationToken).GetAwaiter().GetResult()
        }
        catch {
            throw "Drasi query endpoint '$Uri' could not be reached: $($_.Exception.Message)"
        }

        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            if ($AllowQueryStarting -and
                [int]$response.StatusCode -eq 400 -and
                $body -match 'Query .+ is not running') {
                return [pscustomobject]@{
                    Ready = $false
                    Data = @()
                    Detail = $body
                }
            }

            throw "Drasi query endpoint '$Uri' returned HTTP $([int]$response.StatusCode): $body"
        }

        try {
            $payload = ConvertFrom-Json -InputObject $body -ErrorAction Stop
        }
        catch {
            throw "Drasi query endpoint '$Uri' returned invalid JSON: $($_.Exception.Message)"
        }

        if ($null -eq $payload -or $payload.success -ne $true) {
            $detail = if ($null -ne $payload -and $null -ne $payload.error) {
                $payload.error | ConvertTo-Json -Compress -Depth 5
            }
            else {
                'missing or false success flag'
            }
            throw "Drasi query endpoint '$Uri' reported failure: $detail"
        }

        if (-not ($payload.PSObject.Properties.Name -contains 'data') -or
            $payload.data -isnot [System.Array]) {
            throw "Drasi query endpoint '$Uri' must return an array in 'data'."
        }

        return [pscustomobject]@{
            Ready = $true
            Data = @($payload.data)
            Detail = ''
        }
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
    }
}

function Get-RemainingMilliseconds {
    param(
        [System.Diagnostics.Stopwatch]$Stopwatch,
        [int]$TimeoutSeconds
    )

    return [Math]::Max(
        0,
        [int]([TimeSpan]::FromSeconds($TimeoutSeconds) - $Stopwatch.Elapsed).TotalMilliseconds)
}

$client = $null
$timeoutSource = $null
$sseRequest = $null
$sseResponse = $null
$sseStream = $null
$sseReader = $null

try {
    foreach ($baseUrl in @($ApiBaseUrl, $DrasiBaseUrl)) {
        if (-not [Uri]::IsWellFormedUriString($baseUrl, [UriKind]::Absolute)) {
            throw "Base URL '$baseUrl' must be an absolute HTTP or HTTPS URL."
        }

        $parsedUri = [Uri]$baseUrl
        if ($parsedUri.Scheme -notin @('http', 'https')) {
            throw "Base URL '$baseUrl' must use HTTP or HTTPS."
        }
    }

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $timeoutSource = New-Object System.Threading.CancellationTokenSource
    $timeoutSource.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
    $client = New-Object System.Net.Http.HttpClient

    $apiHealthUri = "$($ApiBaseUrl.TrimEnd('/'))/healthz"
    $drasiHealthUri = "$($DrasiBaseUrl.TrimEnd('/'))/health"
    Assert-HealthEndpoint -Client $client -Name 'API' -Uri $apiHealthUri `
        -CancellationToken $timeoutSource.Token
    Assert-HealthEndpoint -Client $client -Name 'Drasi' -Uri $drasiHealthUri `
        -CancellationToken $timeoutSource.Token

    $queryUri = "$($DrasiBaseUrl.TrimEnd('/'))/api/v1/instances/default/queries/wishlist-updates/results"
    $queryReady = $false
    while (-not $queryReady) {
        if ((Get-RemainingMilliseconds -Stopwatch $timer -TimeoutSeconds $TimeoutSeconds) -le 0) {
            throw "Drasi query endpoint '$queryUri' did not become ready within $TimeoutSeconds seconds."
        }

        $initialResults = Get-DrasiQueryResults -Client $client -Uri $queryUri `
            -CancellationToken $timeoutSource.Token -AllowQueryStarting
        if ($initialResults.Ready) {
            $queryReady = $true
        }
        else {
            Start-Sleep -Milliseconds 250
        }
    }

    $runId = [Guid]::NewGuid().ToString('N')
    $childId = [Guid]::NewGuid().ToString()
    $text = "Aspire self-hosted Drasi smoke $runId"
    $idempotencyKey = "selfhosted-drasi-$runId"
    $sseUri = "$($ApiBaseUrl.TrimEnd('/'))/api/v1/notifications/stream/$childId"
    $wishlistUri = "$($ApiBaseUrl.TrimEnd('/'))/api/v1/children/$childId/wishlist-items"

    $sseRequest = New-Object System.Net.Http.HttpRequestMessage(
        [System.Net.Http.HttpMethod]::Get,
        $sseUri)
    $sseTask = $client.SendAsync(
        $sseRequest,
        [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead,
        $timeoutSource.Token)

    $wishlistPayload = @{
        text = $text
        category = 'toys'
        requestType = 'gift'
    } | ConvertTo-Json -Compress
    $wishlistRequest = New-Object System.Net.Http.HttpRequestMessage(
        [System.Net.Http.HttpMethod]::Post,
        $wishlistUri)
    $wishlistRequest.Headers.TryAddWithoutValidation('Idempotency-Key', $idempotencyKey) | Out-Null
    $wishlistRequest.Content = New-Object System.Net.Http.StringContent(
        $wishlistPayload,
        [System.Text.Encoding]::UTF8,
        'application/json')

    $wishlistResponse = $null
    try {
        try {
            $wishlistResponse = $client.SendAsync(
                $wishlistRequest,
                $timeoutSource.Token).GetAwaiter().GetResult()
        }
        catch {
            throw "Wishlist endpoint '$wishlistUri' could not be reached: $($_.Exception.Message)"
        }

        if (-not $wishlistResponse.IsSuccessStatusCode) {
            $responseBody = $wishlistResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            throw "Wishlist endpoint '$wishlistUri' returned HTTP $([int]$wishlistResponse.StatusCode): $responseBody"
        }
    }
    finally {
        if ($null -ne $wishlistResponse) {
            $wishlistResponse.Dispose()
        }
        $wishlistRequest.Dispose()
    }

    $queryEventObserved = $false
    while (-not $queryEventObserved) {
        if ((Get-RemainingMilliseconds -Stopwatch $timer -TimeoutSeconds $TimeoutSeconds) -le 0) {
            throw "Drasi query endpoint '$queryUri' did not return wishlist item '$text' within $TimeoutSeconds seconds."
        }

        $results = Get-DrasiQueryResults -Client $client -Uri $queryUri `
            -CancellationToken $timeoutSource.Token
        foreach ($item in $results.Data) {
            if ($item.childId -eq $childId -and $item.text -eq $text) {
                $queryEventObserved = $true
                break
            }
        }

        if (-not $queryEventObserved) {
            Start-Sleep -Milliseconds 250
        }
    }

    $remainingMilliseconds = Get-RemainingMilliseconds -Stopwatch $timer -TimeoutSeconds $TimeoutSeconds
    if ($remainingMilliseconds -le 0) {
        throw "SSE endpoint '$sseUri' did not return a notification within $TimeoutSeconds seconds."
    }

    try {
        $sseCompleted = $sseTask.Wait($remainingMilliseconds)
    }
    catch {
        throw "SSE endpoint '$sseUri' failed: $($_.Exception.GetBaseException().Message)"
    }

    if (-not $sseCompleted) {
        throw "SSE endpoint '$sseUri' did not return a notification within $TimeoutSeconds seconds."
    }

    try {
        $sseResponse = $sseTask.GetAwaiter().GetResult()
    }
    catch {
        throw "SSE endpoint '$sseUri' could not be reached: $($_.Exception.Message)"
    }

    if (-not $sseResponse.IsSuccessStatusCode) {
        throw "SSE endpoint '$sseUri' returned HTTP $([int]$sseResponse.StatusCode)."
    }

    if ($sseResponse.Content.Headers.ContentType.MediaType -ne 'text/event-stream') {
        throw "SSE endpoint '$sseUri' returned content type '$($sseResponse.Content.Headers.ContentType.MediaType)'."
    }

    $sseStream = $sseResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $sseReader = New-Object System.IO.StreamReader($sseStream, [System.Text.Encoding]::UTF8)
    $eventName = ''
    $dataLines = New-Object System.Collections.Generic.List[string]
    $sseEventObserved = $false

    while (-not $sseEventObserved) {
        $remainingMilliseconds = Get-RemainingMilliseconds -Stopwatch $timer -TimeoutSeconds $TimeoutSeconds
        if ($remainingMilliseconds -le 0) {
            throw "SSE endpoint '$sseUri' did not deliver wishlist item '$text' within $TimeoutSeconds seconds."
        }

        $lineTask = $sseReader.ReadLineAsync()
        try {
            $lineCompleted = $lineTask.Wait($remainingMilliseconds)
        }
        catch {
            throw "SSE endpoint '$sseUri' stream failed: $($_.Exception.GetBaseException().Message)"
        }

        if (-not $lineCompleted) {
            throw "SSE endpoint '$sseUri' did not deliver wishlist item '$text' within $TimeoutSeconds seconds."
        }

        try {
            $line = $lineTask.GetAwaiter().GetResult()
        }
        catch {
            throw "SSE endpoint '$sseUri' stream failed: $($_.Exception.GetBaseException().Message)"
        }
        if ($null -eq $line) {
            throw "SSE endpoint '$sseUri' closed before delivering wishlist item '$text'."
        }

        if ($line.Length -eq 0) {
            if ($eventName -eq 'notification' -and $dataLines.Count -gt 0) {
                $eventData = ConvertFrom-Json -InputObject ($dataLines -join "`n") -ErrorAction Stop
                if ($eventData.childId -eq $childId -and $eventData.message -like "*$text*") {
                    $sseEventObserved = $true
                }
            }

            $eventName = ''
            $dataLines.Clear()
        }
        elseif ($line.StartsWith('event: ', [StringComparison]::Ordinal)) {
            $eventName = $line.Substring('event: '.Length)
        }
        elseif ($line.StartsWith('data: ', [StringComparison]::Ordinal)) {
            $dataLines.Add($line.Substring('data: '.Length))
        }
    }

    Write-Host "SelfHosted Drasi smoke passed for child '$childId' and wishlist item '$text'."
}
catch {
    [Console]::Error.WriteLine(
        "SelfHosted Drasi validation failed: {0}",
        $_.Exception.Message)
    exit 1
}
finally {
    if ($null -ne $sseReader) {
        $sseReader.Dispose()
    }
    if ($null -ne $sseStream) {
        $sseStream.Dispose()
    }
    if ($null -ne $sseResponse) {
        $sseResponse.Dispose()
    }
    if ($null -ne $sseRequest) {
        $sseRequest.Dispose()
    }
    if ($null -ne $client) {
        $client.Dispose()
    }
    if ($null -ne $timeoutSource) {
        $timeoutSource.Dispose()
    }
}
