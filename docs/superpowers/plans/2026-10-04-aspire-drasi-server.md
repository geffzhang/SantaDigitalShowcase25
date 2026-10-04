# Aspire-managed Drasi Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace SelfHosted's kind/Drasi Platform network path with one Aspire-managed local runtime containing the API, PostgreSQL, Drasi Server, and frontend.

**Architecture:** Aspire starts PostgreSQL with logical replication, the API from `src/Dockerfile`, and the standalone Drasi Server `0.2.3` image on its private resource network. Drasi Server consumes the PostgreSQL outbox with CDC and calls an API reaction endpoint; SelfHosted query consumers use a Drasi Server REST adapter while Azure keeps its existing Drasi Platform/Dapr implementation.

**Tech Stack:** .NET 9, .NET Aspire 13.6.0, PostgreSQL 18.3, Drasi Server 0.2.3, Docker, Pester 3.4.0, xUnit.

**Design:** [Aspire 编排 Drasi Server 设计](../specs/2026-10-04-aspire-drasi-server-design.md)

## Global Constraints

- `src` and AppHost remain `net9.0`; Aspire packages remain `13.6.0`.
- `Runtime:Mode=Azure` remains the default and existing Azure registrations/deployments remain available.
- SelfHosted must not use Azure cloud resources, kind, Kubernetes, Drasi Platform, or Dapr.
- Pin Drasi Server to `ghcr.io/drasi-project/drasi-server:0.2.3`; do not use `latest`.
- Pin PostgreSQL source/bootstrap and HTTP reaction plugins to versions with FFI SDK `0.13.0`, matching Drasi Server `0.2.3`: `source/postgres:0.2.10`, `bootstrap/postgres:0.2.13`, and `reaction/http:0.3.3`.
- PostgreSQL enables logical WAL and enough replication slots/WAL senders for the Drasi source.
- PostgreSQL uses image tag `18.3` to preserve the existing PostgreSQL 18 data volume; Aspire's `WithDataVolume` selects `/var/lib/postgresql` for PostgreSQL 18+.
- SelfHosted migrations ensure the `drasi_wishlist_events` publication includes `public.wishlist_events`; the Drasi PostgreSQL source requires this publication to emit changes.
- PostgreSQL, API, and Drasi Server host-published ports bind only to loopback; containers communicate through Aspire resource endpoints, never static container IPs.
- `LLM_API_KEY`, PostgreSQL credentials, and webhook credentials are injected as secret/environment parameters and never committed or logged.
- Preserve all Drasi query IDs currently consumed by the SelfHosted application; validate translated query fields and time semantics against Drasi Server `0.2.3`.
- Drasi Server query/CDC failures must not become successful empty results or trigger fallback to Kubernetes DNS.
- Keep the full SelfHosted Stage 0 gate blocked until the real PostgreSQL CDC → query → HTTP reaction → persisted notification → SSE/SignalR path passes.
- The checkout already contains unrelated dirty/untracked SelfHosted work. Do not stage or commit shared files wholesale; leave implementation changes unstaged for user review.

---

## File Structure and Boundaries

| File | Responsibility |
|---|---|
| `drasi/selfhosted/server.yaml` | Standalone Drasi Server PostgreSQL source, query definitions, and HTTP reactions; contains environment placeholders only. |
| `tests/scripts/DrasiServerConfig.Tests.ps1` | Fast contract checks for required Drasi source/query/reaction configuration and absence of Kubernetes/Dapr configuration or inline credentials. |
| `src/persistence/WishlistOutboxEvent.cs` | Carries nullable wishlist projection metadata needed by existing Drasi queries. |
| `src/persistence/EntityConfigurations.cs` | Maps the outbox projection metadata to PostgreSQL columns. |
| `src/persistence/Migrations/*WishlistEventProjection*.cs` | Adds nullable metadata columns to existing SelfHosted databases. |
| `src/persistence/Migrations/*DrasiWishlistEventsPublication*.cs` | Ensures the Drasi logical-replication publication includes the wishlist outbox table. |
| `tests/integration/PostgresPersistenceTests.cs` | Verifies migrations create the Drasi publication on PostgreSQL. |
| `src/services/PostgresWishlistRepository.cs` | Copies wishlist metadata to each outbox event in the same transaction. |
| `tests/integration/SelfHostedWishlistRepositoryTests.cs` | Verifies CDC outbox rows retain category, budget, and behavior status. |
| `src/services/DrasiServerViewClient.cs` | Calls Drasi Server's instance/query results REST endpoint, validates its response envelope, and propagates failures. |
| `src/services/DrasiViewClient.cs` | Retains the current Drasi Platform implementation for Azure runtime. |
| `src/lib/RuntimeServiceRegistration.cs` | Registers the Platform or Server query client according to `Runtime:Mode`. |
| `src/Program.cs` | Removes global Drasi client registration; adds the Server reaction endpoint while retaining Dapr endpoints for Azure. |
| `src/services/DrasiHealthCheck.cs` | Uses the active runtime's endpoint and reports Drasi Server reachability without Kubernetes-specific fallback text. |
| `src/services/DrasiDaprSubscriber.cs` | Shares reaction processing between the existing Dapr route and the new Drasi Server HTTP route. |
| `tests/unit/DrasiServerViewClientTests.cs` | Covers URL, query results envelope, cancellation, and all failure shapes. |
| `tests/unit/RuntimeServiceRegistrationTests.cs` | Verifies Azure selects the existing client and SelfHosted selects `DrasiServerViewClient`. |
| `tests/integration/SelfHostedRealtimePipelineTests.cs` | Exercises Server webhook route persistence-before-broadcast and SSE/SignalR behavior. |
| `AppHost/Program.cs` | Orchestrates PostgreSQL, API Dockerfile, Drasi Server container, frontend, internal references, and loopback host endpoints. |
| `AppHost/AppHost.csproj` | Removes the API project reference once AppHost builds API from its Dockerfile. |
| `tests/scripts/validate-selfhosted-drasi.ps1` | Runs live HTTP/CDC/reaction assertions against a running AppHost. |
| `tests/scripts/validate-selfhosted-drasi.Tests.ps1` | Checks smoke-script contracts and its nonzero, endpoint-specific failure behavior. |
| `docs/guides/drasi-self-hosted-validation.md` | Documents the standalone runtime and actual validation evidence. |
| `docs/superpowers/plans/2026-10-04-selfhosted-validation-mode.md` | Updates Stage 0 instructions and preserves its blocked/pass gate semantics. |
| `README.md` | Documents the Aspire-only SelfHosted launch path and external model configuration. |

The following uncommitted kind-only files are superseded and removed in Task 6: `docs/superpowers/plans/2026-10-04-kind-private-network.md`, `drasi/local/kind-network.yaml`, `drasi/local/kind-network-view.yaml`, `tests/scripts/prepare-dev-network.ps1`, `tests/scripts/prepare-dev-network.Tests.ps1`, `tests/scripts/prepare-kind-network-view.ps1`, `tests/scripts/prepare-kind-network-view.Tests.ps1`, and `tests/scripts/validate-dev-network.ps1`. Keep the already-committed kind design specification as historical context; do not revert or stage unrelated dirty files.

---

## Task 1: Define and validate the Drasi Server pipeline configuration

**Files:**
- Create: `drasi/selfhosted/server.yaml`
- Create: `tests/scripts/DrasiServerConfig.Tests.ps1`
- Modify: `src/persistence/Migrations/*DrasiWishlistEventsPublication*.cs`
- Modify: `tests/integration/PostgresPersistenceTests.cs`
- Modify: `src/persistence/WishlistOutboxEvent.cs`
- Modify: `src/persistence/EntityConfigurations.cs`
- Modify: `src/services/PostgresWishlistRepository.cs`
- Modify: `tests/integration/SelfHostedWishlistRepositoryTests.cs`
- Create: EF migration adding nullable `category`, `budget_estimate`, and `status_change` columns to `wishlist_events`

**Interfaces:**
- Consumes the PostgreSQL `wishlist_events` table with primary key column `id`; each event carries the nullable category, budget estimate, and behavior status fields used by the existing projections.
- Ensures the named logical-replication publication includes `public.wishlist_events` before Drasi starts; the source plugin requires a pre-created publication.
- Produces the existing query IDs `wishlist-updates`, `wishlist-trending-1h`, `wishlist-duplicates-global`, `wishlist-inactive-children-3d`, `recommendation-trending-30m`, `wishlist-duplicates-by-child`, and `behavior-status-changes`.
- Sends HTTP reactions to `/api/v1/drasi/reactions/{queryId}` with the payload shape expected by the API.

- [ ] **Step 1: Write failing config contract tests**

Create `tests/scripts/DrasiServerConfig.Tests.ps1`:

```powershell
$configPath = Join-Path $PSScriptRoot '..\..\drasi\selfhosted\server.yaml'

Describe 'Drasi Server SelfHosted configuration' {
    It 'defines the configuration file' {
        Test-Path $configPath | Should Be $true
    }

    It 'uses PostgreSQL CDC for the wishlist outbox primary key' {
        $config = Get-Content -Raw $configPath
        $config | Should Match 'kind:\s*postgres'
        $config | Should Match 'wishlist_events'
        $config | Should Match 'keyColumns:\s*\r?\n\s*-\s*id'
    }

    It 'routes HTTP reactions to the API without Kubernetes or Dapr' {
        $config = Get-Content -Raw $configPath
        $config | Should Match 'kind:\s*http'
        $config | Should Match '/api/v1/drasi/reactions/'
        $config | Should Not Match 'kind:\s*(Dapr|SignalR|SyncDaprStateStore)'
        $config | Should Not Match 'kind:\s*(ContinuousQuery|Reaction)\s*\r?\napiVersion:\s*v1'
    }

    It 'uses environment variables rather than inline credentials' {
        $config = Get-Content -Raw $configPath
        $config | Should Match '\$\{DB_PASSWORD\}'
        $config | Should Not Match '(?im)^\s*(password|apiKey)\s*:\s*[^$]'
    }
}
```

- [ ] **Step 2: Run the contract tests and confirm they fail**

Run: `Invoke-Pester -Script tests\scripts\DrasiServerConfig.Tests.ps1 -EnableExit`

Expected: the file-existence test fails because `drasi/selfhosted/server.yaml` does not exist yet.

- [ ] **Step 3: Create the Drasi Server YAML**

First extend the append-only outbox projection with nullable `category`, `budget_estimate`, and `status_change` columns, map them in EF, copy them from the wishlist entity in the existing repository transaction, and add a forward migration. Add a migration that ensures `drasi_wishlist_events` publishes `public.wishlist_events`: the standalone PostgreSQL source consumes a publication but does not create it. Then use the official Server `0.2.3` schema. Set `apiVersion: drasi.io/v1`, server/instance id `default`, `host: 0.0.0.0`, `port: 8080`, `persistConfig: false`, and `autoInstallPlugins: true`. Configure the PostgreSQL source with `kind: postgres`, `autoStart: true`, `tables: [wishlist_events]`, `tableKeys` mapping `wishlist_events` to `id`, a stable `slotName`/`publicationName`, and a PostgreSQL bootstrap provider. Read connection values from `${DB_HOST}`, `${DB_PORT}`, `${DB_NAME}`, `${DB_USER}`, and `${DB_PASSWORD}`.

Translate the current query contracts from `drasi/resources/drasi-resources.yaml` to Server `queries` using source id `wishlist-postgres`; preserve the existing output aliases required by application code (`item`, `frequency`, `childId`, `duplicateCount`, `lastEvent`, and the wishlist event fields). Configure the HTTP reaction with `baseUrl: "${API_BASE_URL}"` and per-query `outputTemplates.routes`; each added/updated template uses the plugin's `template` field with an explicit JSON envelope:

```yaml
outputTemplates:
  routes:
    wishlist-updates:
      added:
        template: '{"data":{"childId":"{{after.childId}}","text":"{{after.text}}"}}'
```

Add `Content-Type: application/json` and use query-specific templates for each reaction payload. Do not configure Cosmos sync, Dapr pub/sub, Kubernetes resources, or literal secrets.

- [ ] **Step 4: Run Pester and Drasi Server's validator**

Run: `Invoke-Pester -Script tests\scripts\DrasiServerConfig.Tests.ps1 -EnableExit`

Expected: all four configuration contract tests pass.

Run the official validator without printing resolved configuration or real credentials:

```powershell
$configDirectory = (Resolve-Path 'drasi\selfhosted').Path
docker run --rm `
  --mount "type=bind,source=$configDirectory,target=/app/config,readonly" `
  --env DB_HOST=postgres `
  --env DB_PORT=5432 `
  --env DB_NAME=elves `
  --env DB_USER=postgres `
  --env DB_PASSWORD=drasi-test-only `
  --env API_BASE_URL=http://api:80 `
  ghcr.io/drasi-project/drasi-server:0.2.3 `
  validate --config /app/config/server.yaml
```

Expected: Drasi Server exits `0` and reports the configuration valid. If the pinned server rejects a query function or source option, correct the schema/config and preserve the contract tests; do not silently drop a query.

## Task 2: Add a mode-specific Drasi Server query client

**Files:**
- Create: `tests/unit/DrasiServerViewClientTests.cs`
- Create: `src/services/DrasiServerViewClient.cs`
- Modify: `src/lib/RuntimeServiceRegistration.cs`
- Modify: `src/Program.cs`
- Modify: `src/services/DrasiHealthCheck.cs`
- Modify: `tests/unit/RuntimeServiceRegistrationTests.cs`

**Interfaces:**
- Keep `IDrasiViewClient.GetCurrentResultAsync(string queryContainerId, string queryId, CancellationToken ct = default)`.
- Azure maps that interface to the existing `DrasiViewClient`.
- SelfHosted maps it to `DrasiServerViewClient`; `queryContainerId` is the Drasi Server instance id.
- Server endpoint: `GET /api/v1/instances/{instanceId}/queries/{queryId}/results`.
- Server success body: `{"success":true,"data":[...],"error":null}`.

- [ ] **Step 1: Add failing client and registration tests**

In `tests/unit/DrasiServerViewClientTests.cs`, use a test `HttpMessageHandler` and cover:

1. A successful envelope returns each object in `data` as a `JsonNode`.
2. The request path escapes both `queryContainerId` and `queryId` and ends with `/api/v1/instances/default/queries/wishlist-trending-1h/results`.
3. A non-2xx status, `success: false`, missing/non-array `data`, and malformed JSON throw; none return an empty list.
4. `OperationCanceledException` propagates.

Extend `RuntimeServiceRegistrationTests` so Azure resolves `DrasiViewClient` and SelfHosted resolves `DrasiServerViewClient`.

- [ ] **Step 2: Run the targeted tests and confirm they fail**

Run: `dotnet test tests\Tests.csproj --filter "FullyQualifiedName~DrasiServerViewClientTests|FullyQualifiedName~RuntimeServiceRegistrationTests" --no-restore`

Expected: the new type is missing and SelfHosted still resolves the platform client.

- [ ] **Step 3: Implement the Server adapter**

Implement `DrasiServerViewClient` with `HttpClient`, `IConfiguration`, and `ILogger<DrasiServerViewClient>`. Require `Drasi:ServerBaseUrl`/`DRASI_SERVER_BASE_URL`; build the escaped instance/query URI; call `EnsureSuccessStatusCode`; parse the `success`, `data`, and `error` properties; throw a descriptive exception for false/missing success, missing data, invalid result shape, or invalid JSON. Do not catch transport/JSON exceptions and return an empty list.

- [ ] **Step 4: Register the correct client and update Drasi health diagnostics**

Move the global `AddHttpClient<IDrasiViewClient, DrasiViewClient>` registration out of `src/Program.cs` and into runtime registration. Keep the existing Platform client and resilience policy for Azure. Register `DrasiServerViewClient` with the same timeout/resilience policy for SelfHosted. Update `DrasiHealthCheck` to use the active runtime's base URL and remove K8s DNS/LoadBalancer hints; keep `/healthz` independent of Drasi so Aspire can use it as the API migration-ready signal.

- [ ] **Step 5: Run targeted tests and build**

Run: `dotnet test tests\Tests.csproj --filter "FullyQualifiedName~DrasiServerViewClientTests|FullyQualifiedName~RuntimeServiceRegistrationTests" --no-restore`

Expected: all adapter and mode-selection tests pass.

Run: `dotnet build src\src.csproj --no-restore --nologo`

Expected: build succeeds without new warnings or errors.

## Task 3: Add the Drasi Server HTTP reaction route

**Files:**
- Modify: `src/services/DrasiDaprSubscriber.cs`
- Modify: `src/Program.cs`
- Modify: `tests/integration/SelfHostedRealtimePipelineTests.cs`

**Interfaces:**
- Preserve existing Azure/Dapr routes: `GET /api/v1/dapr/subscribe` and `POST /api/v1/dapr/drasi/{queryId}`.
- Add `POST /api/v1/drasi/reactions/{queryId}` for Drasi Server HTTP reactions.
- Both routes process the same event contract and notification persistence/broadcast sequence.

- [ ] **Step 1: Add failing route tests**

Extend `SelfHostedRealtimePipelineTests` with tests that post `{"data":{"childId":"child-1","text":"Wind-up train"}}` to `/api/v1/drasi/reactions/wishlist-updates`. Assert HTTP `202 Accepted`, notification persistence before publish, one SSE `notification` event, and one SignalR notification. Add invalid payload and repository-failure cases asserting 4xx/5xx and no broadcast.

- [ ] **Step 2: Run the targeted integration tests and confirm the route is missing**

Run: `dotnet test tests\Tests.csproj --filter FullyQualifiedName~SelfHostedRealtimePipelineTests --no-restore`

Expected: the new route tests fail with `404`; existing Dapr route tests remain unchanged.

- [ ] **Step 3: Share reaction processing between both routes**

Refactor the existing `DrasiDaprSubscriber` endpoint delegate into one private/shared handler. Map it to both the existing Dapr route and the new `drasi/reactions/{queryId}` route. Keep JSON validation, notification mapping, persistence-before-publish ordering, cancellation propagation, structured error logging, and status codes unchanged.

- [ ] **Step 4: Run the targeted pipeline tests**

Run: `dotnet test tests\Tests.csproj --filter FullyQualifiedName~SelfHostedRealtimePipelineTests --no-restore`

Expected: Server reaction tests and existing Dapr tests pass; a persistence failure never publishes SSE/SignalR.

## Task 4: Orchestrate API, PostgreSQL, and Drasi Server in Aspire

**Files:**
- Modify: `AppHost/Program.cs`
- Modify: `AppHost/AppHost.csproj`

**Interfaces:**
- Aspire resources: `postgres`, database `elves`, `api`, `drasi-server`, and existing `frontend`.
- API container port `80`; PostgreSQL port `5432`; Drasi Server API port `8080`.
- Host-published ports default to API `8081`, PostgreSQL `5433`, and Drasi Server `8080`, each verified as loopback-only.
- PostgreSQL database and model secrets are passed using Aspire secret parameters.

- [ ] **Step 1: Configure PostgreSQL for CDC and preserve the existing AppHost topology**

Use Aspire's PostgreSQL 18.3 resource with a secret password parameter, persistent data volume, and server arguments. Set the image tag before `WithDataVolume` so Aspire mounts the PostgreSQL 18+ data path and reuses the existing data volume:

```csharp
.WithArgs(
    "-c", "wal_level=logical",
    "-c", "max_replication_slots=10",
    "-c", "max_wal_senders=10")
```

Keep database resource `elves`, the API connection-string reference, and existing Vite frontend.

- [ ] **Step 2: Replace the API process resource with its existing Dockerfile resource**

Build API from `src/Dockerfile`; set `Runtime__Mode=SelfHosted`, `ASPNETCORE_URLS=http://0.0.0.0:80`, existing model endpoint/model/secret values, and PostgreSQL reference. Publish container port `80` to the configured host API port and verify the host binding is loopback. Attach an HTTP health check to `/healthz`; this endpoint becomes available only after startup migrations complete.

- [ ] **Step 3: Add the Drasi Server container**

Add `ghcr.io/drasi-project/drasi-server:0.2.3` as resource `drasi-server`. Mount `drasi/selfhosted` read-only at `/app/config`, set DB and API callback environment variables from Aspire resource endpoints/secret parameters, and configure `DRASI_SERVER_BASE_URL` for the API through the Drasi resource endpoint reference. Set `--plugins-dir /tmp/drasi-plugins` because the image runs as an unprivileged user and its binary directory is not writable. Publish Drasi Server port `8080` on loopback for local UI/API diagnostics.

Make Drasi Server wait for the API `/healthz` migration-ready endpoint, and make API wait only for PostgreSQL. Do not make API wait for Drasi, avoiding a resource dependency cycle while still ensuring the CDC source starts after migrations.

- [ ] **Step 4: Remove the obsolete AppHost project reference**

Remove `ProjectReference` to `src/src.csproj` from `AppHost/AppHost.csproj`; retain Aspire Hosting, JavaScript, and PostgreSQL package references.

- [ ] **Step 5: Build and verify AppHost resource configuration**

Run:

```powershell
dotnet build AppHost\AppHost.csproj --no-restore --nologo
```

Expected: AppHost builds successfully. Start it with the developer's existing Aspire/user-secret configuration; do not put model or database secret values in a command, committed file, or output.

Verify `/healthz`, Drasi Server `/health`, and PostgreSQL readiness. Inspect only container names, network membership, and `NetworkSettings.Ports` (never dump container environment variables). Confirm API, PostgreSQL, and Drasi Server host mappings are loopback-only.

## Task 5: Validate the live SelfHosted CDC and reaction path

**Files:**
- Create: `tests/scripts/validate-selfhosted-drasi.ps1`
- Create: `tests/scripts/validate-selfhosted-drasi.Tests.ps1`
- Modify: `tests/integration/SelfHostedRealtimePipelineTests.cs` only if live findings expose a contract defect.

**Interfaces:**
- Script parameters: `ApiBaseUrl` default `http://localhost:8081`, `DrasiBaseUrl` default `http://localhost:8080`, and `TimeoutSeconds` default `60`.
- The script assumes AppHost is running and never prints environment variables or secrets.

- [x] **Step 1: Write failing script contract tests**

Add Pester tests for:

1. A missing/unreachable API or Drasi Server fails with a nonzero result and names the failed endpoint.
2. A successful Drasi REST response must have `success: true` and array `data`.
3. The smoke event is unique per run and the script checks the same event in query results and the child notification SSE stream.

Run: `Invoke-Pester -Script tests\scripts\validate-selfhosted-drasi.Tests.ps1 -EnableExit`

Expected: tests fail because the validation script does not exist.

- [x] **Step 2: Implement the live smoke script**

The script must:

1. Check `GET /healthz` on the API and `GET /health` on Drasi Server.
2. Poll `GET /api/v1/instances/default/queries/wishlist-updates/results` until the configured query is running; reject non-2xx responses, `success: false`, missing data, or non-array data.
3. Open `/api/v1/notifications/stream/{uniqueChildId}` before creating the event.
4. POST a unique gift wishlist item to `/api/v1/children/{childId}/wishlist-items` with JSON containing `text`, `category`, and `requestType: "gift"` and an `Idempotency-Key`.
5. Wait until the same item appears in Drasi query results and until the SSE stream contains `event: notification` with that child id/item.
6. Dispose HTTP response/stream resources and return nonzero on timeout or any failed assertion.

- [x] **Step 3: Run Pester script tests**

Run: `Invoke-Pester -Script tests\scripts\validate-selfhosted-drasi.Tests.ps1 -EnableExit`

Expected: all script failure and response-contract tests pass.

- [x] **Step 4: Run the full local integration gate**

With Docker available and AppHost started using configured secrets, run:

```powershell
.\tests\scripts\validate-selfhosted-drasi.ps1
dotnet test tests\Tests.csproj --no-restore
```

Expected: one unique event is observed through PostgreSQL CDC and the Drasi query, the API reaction persists and broadcasts its notification, and the unit/integration suite passes. If the pinned Drasi image or any translated query fails, record the exact failing boundary and keep Stage 0 **Blocked**.

## Task 6: Update documentation and retire the kind-only path

**Files:**
- Modify: `README.md`
- Modify: `docs/guides/drasi-self-hosted-validation.md`
- Modify: `docs/superpowers/plans/2026-10-04-selfhosted-validation-mode.md`
- Delete the kind-only uncommitted files listed in File Structure and Boundaries.
- Do not modify or delete: committed `docs/superpowers/specs/2026-10-04-kind-private-network-design.md`.

- [x] **Step 1: Update the SelfHosted launch and validation documentation**

Document one Aspire command to start the local stack; pinned Drasi Server version; required model configuration; PostgreSQL logical replication; local API/Drasi endpoints; and how to run the live Pester smoke gate. State that Azure continues to use its existing runtime. Record only observed test results, and keep Stage 0 blocked until Task 5's complete gate passes.

- [x] **Step 2: Remove only the superseded uncommitted kind artifacts**

Delete the exact paths listed in File Structure and Boundaries. Before each deletion, confirm the path is still untracked and matches the task-created kind implementation; do not stage, revert, or otherwise alter unrelated changes in the dirty worktree.

- [x] **Step 3: Run final documentation and regression checks**

Run:

```powershell
dotnet build AppHost\AppHost.csproj --no-restore --nologo
dotnet test tests\Tests.csproj --no-restore
Invoke-Pester -Script tests\scripts\DrasiServerConfig.Tests.ps1 -EnableExit
Invoke-Pester -Script tests\scripts\validate-selfhosted-drasi.Tests.ps1 -EnableExit
```

Expected: all commands pass. The separate live smoke gate must have passed before updating documentation to report Stage 0 as passed.

- [x] **Step 4: Leave all implementation changes unstaged for review**

Run `git status --short` and confirm no files were staged or committed by this plan. Report the new/modified/deleted task-owned paths separately from the pre-existing dirty SelfHosted work. The user can decide how to stage or commit after reviewing the combined worktree.
