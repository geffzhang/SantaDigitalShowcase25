$configPath = Join-Path $PSScriptRoot '..\..\drasi\selfhosted\server.yaml'
$config = if (Test-Path $configPath) { Get-Content -Raw $configPath } else { '' }

Describe 'Drasi Server SelfHosted configuration' {
    It 'defines the configuration file' {
        Test-Path $configPath | Should Be $true
    }

    It 'pins plugins to versions compatible with Drasi Server 0.2.3' {
        $config | Should Match 'ref:\s*source/postgres:0\.2\.10'
        $config | Should Match 'ref:\s*bootstrap/postgres:0\.2\.13'
        $config | Should Match 'ref:\s*reaction/http:0\.3\.3'
    }

    It 'uses PostgreSQL CDC for the wishlist outbox primary key' {
        $config | Should Match 'kind:\s*postgres'
        $config | Should Match 'wishlist_events'
        $config | Should Match 'keyColumns:\s*\r?\n\s*-\s*id'
    }

    It 'routes HTTP reactions to the API without Kubernetes or Dapr' {
        $config | Should Match 'kind:\s*http'
        $config | Should Match '/api/v1/drasi/reactions/'
        $config | Should Not Match 'kind:\s*(Dapr|SignalR|SyncDaprStateStore)'
        $config | Should Not Match 'kind:\s*(ContinuousQuery|Reaction)\s*\r?\napiVersion:\s*v1'
    }

    It 'uses the HTTP plugin outputTemplates schema for query-specific request bodies' {
        $config | Should Match '(?s)outputTemplates:\s+routes:.*wishlist-trending-1h:.*added:.*template:'
        $config | Should Not Match '(?m)^\s+body:'
    }

    It 'sends wishlist CDC changes through the notification reaction' {
        $reactionConfig = ($config -split 'reactions:\s*', 2)[1]
        $reactionConfig | Should Match '(?s)queries:\s+- wishlist-updates'
        $reactionConfig | Should Match '(?s)outputTemplates:.*wishlist-updates:.*template:'
    }

    It 'uses environment variables rather than inline credentials' {
        $config | Should Match '\$\{DB_PASSWORD\}'
        $credentialLines = $config -split '\r?\n' | Where-Object { $_ -match '^\s*(password|apiKey)\s*:' }
        $credentialLines | Should Not BeNullOrEmpty
        foreach ($line in $credentialLines) {
            $line | Should Match '^\s*(password|apiKey)\s*:\s*"\$\{[^}]+\}"\s*$'
        }
    }

    It 'retains metadata required by the existing wishlist and behavior queries' {
        $config | Should Match 'category'
        $config | Should Match 'budget_estimate'
        $config | Should Match 'status_change'
        $config | Should Match 'wishlist-updates'
        $config | Should Match 'behavior-status-changes'
    }

    It 'uses supported aggregation for per-child wishlist duplicates' {
        $duplicateQuery = ($config -split '(?m)^\s*- id: wishlist-duplicates-by-child\s*$')[1] `
            -split '(?m)^\s*- id: behavior-status-changes\s*$' |
            Select-Object -First 1

        $duplicateQuery | Should Match 'count\(w\) AS duplicateCount'
        $duplicateQuery | Should Not Match 'count\(\*\)'
    }

    It 'preserves every query id consumed by the SelfHosted application' {
        $queryIds = @(
            'wishlist-updates',
            'wishlist-trending-1h',
            'wishlist-duplicates-global',
            'wishlist-inactive-children-3d',
            'recommendation-trending-30m',
            'wishlist-duplicates-by-child',
            'behavior-status-changes'
        )

        foreach ($queryId in $queryIds) {
            $config | Should Match "(?m)^\s+- id: $queryId\s*$"
        }
    }
}
