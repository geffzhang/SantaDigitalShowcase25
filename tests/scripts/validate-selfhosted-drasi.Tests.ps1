$scriptPath = Join-Path $PSScriptRoot 'validate-selfhosted-drasi.ps1'
$scriptContent = if (Test-Path $scriptPath) { Get-Content -Raw $scriptPath } else { '' }

Describe 'SelfHosted Drasi live smoke script' {
    It 'defines the validation script' {
        Test-Path $scriptPath | Should Be $true
    }

    It 'provides local API, Drasi, and timeout defaults' {
        $scriptContent | Should Match '\$ApiBaseUrl\s*=\s*''http://localhost:8081'''
        $scriptContent | Should Match '\$DrasiBaseUrl\s*=\s*''http://localhost:8080'''
        $scriptContent | Should Match '\$TimeoutSeconds\s*=\s*60'
    }

    It 'checks API and Drasi health and validates query result envelopes' {
        $scriptContent | Should Match '/healthz'
        $scriptContent | Should Match '/health'
        $scriptContent | Should Match 'success'
        $scriptContent | Should Match 'data.*Array'
        $scriptContent | Should Match 'payload\.success -ne \$true'
        $scriptContent | Should Match 'payload\.data -isnot \[System\.Array\]'
    }

    It 'uses one unique wishlist event to verify both Drasi and SSE delivery' {
        $scriptContent | Should Match 'Guid\]::NewGuid'
        $scriptContent | Should Match 'Idempotency-Key'
        $scriptContent | Should Match 'wishlist-updates/results'
        $scriptContent | Should Match 'notifications/stream'
        $scriptContent | Should Match '\$eventName -eq ''notification'''
        $scriptContent | Should Match 'childId'
    }

    It 'opens the SSE stream before submitting the wishlist event' {
        $streamIndex = $scriptContent.IndexOf('notifications/stream')
        $postIndex = $scriptContent.IndexOf('wishlist-items')

        ($streamIndex -ge 0 -and $postIndex -gt $streamIndex) | Should Be $true
    }

    It 'reports failed endpoint URLs instead of hiding validation errors' {
        $scriptContent | Should Match '\$Name health endpoint'
        $scriptContent | Should Match 'Drasi query endpoint'
    }

    It 'returns a nonzero exit and names the URL when the API is unreachable' {
        $powershell = (Get-Process -Id $PID).Path
        $output = & $powershell -NoProfile -File $scriptPath `
            -ApiBaseUrl 'http://127.0.0.1:1' `
            -DrasiBaseUrl 'http://127.0.0.1:1' `
            -TimeoutSeconds 2 2>&1

        $LASTEXITCODE | Should Not Be 0
        ($output -join "`n") | Should Match 'API health endpoint ''http://127\.0\.0\.1:1/healthz'''
    }
}
