# Drasi SelfHosted validation

The SelfHosted runtime is managed by .NET Aspire and does not require Azure, kind, Kubernetes, Dapr, or the Drasi CLI. The existing Azure runtime and deployment remain available separately.

## Local topology

Run `dotnet run --project AppHost\AppHost.csproj` from the repository root. Aspire starts:

| Component | Version or endpoint |
| --- | --- |
| API | `http://localhost:8081` |
| PostgreSQL | `postgres:18.3`, host port `5433` |
| Drasi Server | `ghcr.io/drasi-project/drasi-server:0.2.3`, `http://localhost:8080` |
| PostgreSQL source plugin | `source/postgres:0.2.10` |
| PostgreSQL bootstrap plugin | `bootstrap/postgres:0.2.13` |
| HTTP reaction plugin | `reaction/http:0.3.3` |
| Frontend | Aspire-managed Vite development server |

Published host ports bind to loopback. Containers communicate over the Aspire resource network. PostgreSQL uses a persistent volume; the configured major version is 18.3 to retain PostgreSQL 18 data.

## Configuration

Configure a user-controlled OpenAI-compatible model endpoint and local PostgreSQL password as AppHost user secrets. Never commit or print secret values:

```powershell
dotnet user-secrets set "LLM_BASE_URL" "https://<your-model-host>/v1" --project AppHost
dotnet user-secrets set "LLM_MODEL_NAME" "<model-name>" --project AppHost
dotnet user-secrets set "LLM_API_KEY" "<model-api-key>" --project AppHost
dotnet user-secrets set "Parameters:postgres-password" "<local-postgres-password>" --project AppHost
```

The SelfHosted API applies its EF migrations before it is ready. A migration creates the `drasi_wishlist_events` PostgreSQL publication for `public.wishlist_events`; PostgreSQL logical WAL, the publication, and the replication slot are required for Drasi CDC. Drasi Server consumes only that outbox table, runs the configured queries, and posts HTTP reactions to the API.

## Validation

Run the contract tests, live smoke, and .NET suite:

```powershell
Invoke-Pester -Script tests\scripts\DrasiServerConfig.Tests.ps1 -EnableExit
Invoke-Pester -Script tests\scripts\validate-selfhosted-drasi.Tests.ps1 -EnableExit
.\tests\scripts\validate-selfhosted-drasi.ps1
dotnet test tests\Tests.csproj --no-restore
```

The smoke script checks API and Drasi health, waits for `wishlist-updates`, opens the child notification SSE request, and submits one uniquely keyed gift wishlist item. It passes only when the same child and text appear in Drasi query results and in an SSE `notification`; the reaction handler persists the notification before publishing it. It returns a nonzero exit code and identifies the failed endpoint or assertion on failure.

**Last verified:** 2026-10-04. The live smoke passed with the default timeout; the matching wishlist event appeared in Drasi results and SSE, and a direct PostgreSQL check confirmed the reaction's matching notification row. All seven configured query result endpoints returned successful envelopes. AppHost build completed with 0 warnings and 0 errors; the .NET suite passed 108 tests with 2 skipped; the combined Pester suites passed 17 tests. The .NET integration tests cover persistence-before-acknowledgement plus SSE and SignalR broadcasting.

Run the commands above after changes; a successful build alone is not evidence of the live CDC path.
