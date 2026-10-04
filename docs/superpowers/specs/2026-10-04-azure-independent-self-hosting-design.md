# Azure-independent self-hosting: assessment and target design

**Date:** 2026-10-04  
**Status:** Proposed; architecture direction approved, detailed specification awaiting user review

## 1. Goal and scope

Make the application run on a developer computer and on an owner-operated Linux host without Azure accounts, Azure-hosted services, or Azure credentials. Preserve the React frontend, .NET API and business flows, Drasi continuous queries, real-time updates, and AI-agent/tool-calling experience.

The default runtime will use self-hosted components. Local model inference is in scope; using an external non-Azure AI service is not required. Initial setup may need to download container images and model weights, but normal operation must not call Azure. A developer machine and the Linux host must use the same application and Drasi deployment manifests. The local setup may bootstrap a local Kubernetes cluster; the Linux setup may target an existing K3s or standard Kubernetes cluster.

This assessment does not include migrating data from an existing Azure production environment, high-availability across multiple hosts, or a specific model/hardware benchmark. If existing cloud data must be retained, a separate export/import and cutover requirement is needed.

## 2. Current-state findings

- The API registers Cosmos DB as its primary persistence implementation. `ICosmosRepository.GetContainer` exposes the Cosmos SDK `Container`, and five domain repositories inherit `CosmosRepositoryBase<T>`. If Cosmos configuration is absent, `Program.cs` attempts the Key Vault setup path and throws when it cannot create a Cosmos client. The repository boundary therefore does not currently permit a drop-in database configuration change.
- Wishlist and recommendation changes are read from Cosmos DB Change Feed and published to Event Hubs. Drasi also has an Event Hubs source configured with Microsoft Entra workload identity. The Drasi graph includes a `SyncDaprStateStore` reaction targeting a Cosmos state store. Some API paths publish events directly as well, so the migration must identify and preserve the canonical event semantics and deduplication behavior.
- Azure OpenAI client construction appears in the main dependency registrations and in additional agent/API code paths. A single configurable model-provider boundary is not yet used consistently.
- `infra/main.bicep` provisions Azure OpenAI, Cosmos DB, Event Hubs, Azure Container Apps, AKS, Key Vault, ACR, and Azure Monitor resources. `azure.yaml` and deployment scripts orchestrate these Azure resources. The repository has Kubernetes manifests for Drasi but no Docker Compose definition for a full local stack.

Relevant implementation and deployment files:

- [src/Program.cs](../../../src/Program.cs)
- [src/services/CosmosRepository.cs](../../../src/services/CosmosRepository.cs)
- [src/services/CosmosRepositoryBase.cs](../../../src/services/CosmosRepositoryBase.cs)
- [src/services/CosmosChangeFeedService.cs](../../../src/services/CosmosChangeFeedService.cs)
- [src/services/EventHubPublisher.cs](../../../src/services/EventHubPublisher.cs)
- [src/services/MultiAgentOrchestrator.cs](../../../src/services/MultiAgentOrchestrator.cs)
- [drasi/resources/drasi-resources.yaml](../../../drasi/resources/drasi-resources.yaml)
- [drasi/sources/eventhub-source.yaml](../../../drasi/sources/eventhub-source.yaml)
- [infra/main.bicep](../../../infra/main.bicep)
- [azure.yaml](../../../azure.yaml)

## 3. Alternatives considered

### A. Keep Drasi and agent behavior; replace Azure services — recommended

Move persistent application data to PostgreSQL, move model calls to a local OpenAI-compatible inference service behind a single provider boundary, and run Drasi plus the API and frontend on local/owner-operated Kubernetes. Replace the Event Hubs input and Cosmos state reaction with Drasi-supported self-hosted integrations.

This best matches the stated goals and preserves the showcase's main experience. It is a substantial data/event/deployment refactor. Drasi adapter compatibility is a release gate, not an assumption.

### B. Use Azure local emulators as a transition

Use local Cosmos DB and Event Hubs emulators with local Kubernetes and a local model. This reduces early API/storage changes and can help exercise some Azure SDK paths. It does not produce a suitable long-term self-hosted server architecture: emulator feature sets are limited, and the Event Hubs emulator documentation explicitly excludes production use. This is an optional short-lived development aid, not the target design.

References: [Azure Cosmos DB Linux emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux) and [Azure Event Hubs emulator limitations](https://learn.microsoft.com/en-us/azure/event-hubs/overview-emulator).

### C. Replace Drasi with application-managed projections

Use the self-hosted database and application workers to calculate real-time projections and notify clients. This can simplify deployment, but it removes the Drasi-based continuous-query experience the user wants to retain. It is a fallback only if the Drasi self-hosting compatibility gate fails and the user explicitly approves the behavior change.

## 4. Recommended target architecture

```text
Browser
  |
  v
React frontend + ASP.NET Core API
  |                         |
  |                         +--> Local OpenAI-compatible model server
  |                              (tool calling + streaming required)
  |
  +--> PostgreSQL
         |  application data and durable event/outbox records
         |
         +--> Drasi-compatible self-hosted source (choice gated)
                    |
                    v
                 Drasi on Kubernetes
                    |             |
                    |             +--> Existing SignalR/SSE-facing updates
                    +--> Self-hosted result/state reaction (choice gated)
```

### Application and data

Keep API routes, DTOs, and business-service contracts stable where possible. Move Cosmos SDK usage behind domain-oriented persistence interfaces that do not expose Cosmos types. PostgreSQL is the recommended persistent store because it can run locally and on Linux hosts with durable volumes and established backup tooling.

Use a transactional outbox for application events so that a committed business write cannot silently lose its Drasi event. Preserve the current event fields, schema versioning, idempotency, and deduplication semantics. Do not use the existing in-memory repositories as a production persistence fallback.

### Drasi and event integration

Retain the current continuous-query definitions and the client-facing real-time contract. The current Event Hubs source and Cosmos Dapr-state reaction must be replaced. First test a maintained Drasi self-hosted source against the target event representation. PostgreSQL change data capture is the first candidate if supported by the selected Drasi release; a self-hosted broker is a second candidate only if a compatible Drasi source is available. For query results, prefer a supported self-hosted Dapr state component or an application-facing reaction that preserves existing API behavior.

If no supported input and result path can preserve the required queries and event semantics, stop before the main migration and return to the user with the compatibility findings and the choice between a custom Drasi integration and the explicitly non-preferred Drasi replacement.

### AI and secrets

Route every Agent Framework call path through one configured chat-client/provider boundary using an OpenAI-compatible local inference endpoint. Validate streaming, structured/tool calls, cancellation, timeouts, and error reporting against the selected model. Do not silently fall back to canned responses when model inference is unavailable.

Use environment configuration for local development and mounted secrets for the Linux host. Azure Key Vault and managed identity are not runtime requirements.

### Deployment and operations

Use a shared Kubernetes base (for example, Kustomize) for the API, frontend, database, local model endpoint, and Drasi integrations. A developer bootstrap command may create a local kind/k3d cluster; Linux deployment targets K3s or standard Kubernetes using the same manifests. This deliberately favors deployment consistency over a Docker Compose-only design because this repository's Drasi deployment is Kubernetes-based.

Provide persistent volumes, readiness checks, database initialization/migrations, backup/restore instructions, and actionable logs. A local startup command must report prerequisite failures (container runtime, cluster, model assets, or configuration) rather than continuing with a partial success state. Azure Bicep/azd files may be archived or removed from the default deployment path; they must not be required to build, start, test, or operate the self-hosted target.

## 5. Delivery stages and decision gates

### Stage 0 — Compatibility spike

1. Start the selected Drasi release on a local Kubernetes cluster.
2. Prove one self-hosted input path and one non-Cosmos result/state path with representative wishlist events.
3. Run the existing critical Drasi queries and verify their result shape and ordering/deduplication expectations.
4. Verify SignalR/SSE delivery to the existing frontend.
5. Verify that the local model endpoint supports the Agent Framework tool calls and streaming used by the application.

**Gate:** Do not begin the broad storage/event migration until all five checks pass or the user approves a documented behavior change.

### Stage 1 — Provider boundaries

Remove Cosmos-specific SDK types from business repository contracts. Introduce a PostgreSQL-backed implementation, explicit schema/migrations, and persistence integration tests. Centralize chat-client construction and remove direct Azure OpenAI construction from other call paths. Configuration validation must clearly identify missing database, event, or model settings.

### Stage 2 — Durable event and Drasi path

Implement the transactional outbox and the selected Drasi-compatible source/reaction. Remove Cosmos Change Feed, Event Hubs, Cosmos state-store, and Azure identity dependencies from the runtime path. Test restart recovery, retries, duplicate delivery, and error propagation.

### Stage 3 — Self-hosted packaging

Add shared Kubernetes manifests, local cluster bootstrap, Linux deployment instructions, secret and volume configuration, readiness checks, backup/restore steps, and a complete local integration-test profile. Update the root and service documentation to describe the Azure-independent path as the default.

## 6. Acceptance criteria

1. A clean developer machine with the documented container and local Kubernetes prerequisites can start the full stack using one documented command, without Azure login, Azure CLI, azd, Azure endpoint variables, or Azure resources.
2. The Linux host can deploy the same application and Drasi manifests to K3s or standard Kubernetes without adding Azure-managed services.
3. API writes persist in PostgreSQL and remain after API/database pod restarts, subject to the configured persistent volume.
4. A representative wishlist write is durably recorded, processed by Drasi, reflected in the relevant continuous-query output, and delivered over the existing real-time API contract.
5. AI-agent streaming and required tool calls work against the configured local model. Model startup/unavailability is surfaced as a clear health or request error.
6. The test suite covers persistence, event/outbox behavior, Drasi query/result contracts, client streaming, and local model integration. No test treats process-local in-memory state as durable storage.
7. The self-hosted runtime has no required Azure SDK credentials, Azure service endpoints, Azure cloud resource deployment, or hidden Azure network calls. Historical Azure documentation may remain clearly marked as legacy.
8. Startup, readiness, logs, and deployment scripts identify missing dependencies and failed components explicitly; they do not report a successful full deployment when a required component is unavailable.

## 7. Main risks and controls

| Risk | Impact | Control |
|---|---|---|
| Drasi release lacks a maintained self-hosted source or result adapter for the chosen path | Blocks the preferred architecture or requires custom integration | Make compatibility a Stage 0 gate; test source and reaction before repository-wide changes |
| Cosmos types and Change Feed patterns are spread through persistence and event publishing | Larger rewrite and possible behavioral drift | Establish provider-neutral contracts first; preserve API/event contract tests |
| Multiple current event-publishing paths may produce overlapping events | Duplicate or reordered Drasi results | Document canonical write-to-event flow and deduplication before replacing publishers |
| Local model performance or tool-call support varies by model and hardware | Agent feature regressions or poor latency | Pin a tested model/runtime profile and run tool-call/streaming integration tests on target hardware |
| Local Kubernetes and model assets have significant resource requirements | Developer setup is heavier than the current API-only local run | Publish minimum resource guidance, readiness checks, and explicit image/model download steps |
| Existing Azure deployment scripts and docs imply Azure is mandatory | Users continue following the old path | Make self-hosted setup the primary quickstart and label Azure assets as optional legacy material |

## 8. Assessment conclusion

The project can be converted to an Azure-independent, self-hosted application, but this is a high-complexity modernization rather than a deployment-template swap. The most consequential work is replacing the Cosmos-specific persistence and Change Feed path while retaining Drasi behavior. The recommended sequence is to prove Drasi compatibility first, then refactor storage and AI providers, then package both developer and Linux-host deployments on Kubernetes. Local emulators may shorten some development loops but do not satisfy the long-term self-hosted-server goal.
