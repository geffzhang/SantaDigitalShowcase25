# SelfHosted 阶段 0A 验证模式实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不删除现有 Azure 路径的前提下，新增可选的 SelfHosted runtime，让真实 wishlist API、PostgreSQL/outbox、Drasi、通知 API 及 SSE/SignalR 形成可验证的端到端链路。

**Architecture:** `Runtime:Mode=Azure` 在迁移期间保持默认并继续使用当前注册；显式选择 `Runtime:Mode=SelfHosted` 时，API 改用 PostgreSQL 持久化与标准 OpenAI-compatible `IChatClient`，并跳过 Cosmos、Key Vault、Event Hubs 和 Azure OpenAI 服务。Aspire 启动 API/PostgreSQL，Drasi/Dapr 运行在 kind；模型服务由用户控制，可在开发机或远端主机运行。

**Tech Stack:** .NET 9、EF Core 9.0.20、Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4、Testcontainers.PostgreSql 4.15.0、.NET Aspire 13.6.0、Drasi/Dapr 0.10.0/1.14.5、kind、OpenAI-compatible API。

## Global Constraints

- `src` 和 AppHost 保持 `net9.0`；Aspire package 固定为 `13.6.0`。
- 迁移期间默认行为保持 Azure；只有显式配置 `Runtime:Mode=SelfHosted` 才启用本计划的自托管路径。
- 此计划及阶段 0 完整闸门通过前，不删除 Azure DI、Azure package、Bicep 或 Azure 部署路径。
- SelfHosted 模式不得解析 Azure 凭据或回退到 Cosmos、Event Hubs、Azure OpenAI、Key Vault 或内存业务数据存储。
- PostgreSQL wishlist 业务记录与 Drasi outbox event 在同一事务提交；通知持久化成功后才向 SSE/SignalR 发布成功事件。
- `LLM_BASE_URL`、`LLM_MODEL_NAME`、`LLM_API_KEY` 仅作为环境配置输入；API key 不得写入仓库、日志、dashboard 输出或测试报告。
- Drasi 0.10.0 已验证 PostgreSQL source、HTTP reaction；已安装 provider 列表没有 MongoDB source，本阶段固定使用 PostgreSQL。
- 端到端失败必须明确返回错误或停止启动，不得以内存 fallback、静默 catch 或 success-shaped 结果绕过。

---

## 文件结构与边界

- 创建 `src/lib/RuntimeModeOptions.cs`、`src/lib/RuntimeServiceRegistration.cs`：解析 Azure/SelfHosted 模式并按模式注册服务。
- 修改 `src/appsettings.json`：显式记录迁移期间默认的 `Runtime:Mode=Azure`，不添加任何密钥。
- 创建 `src/persistence/AppDbContext.cs`、`src/persistence/AppDbContextFactory.cs`、`src/persistence/EntityConfigurations.cs`、`src/persistence/Migrations/`：本阶段所需 PostgreSQL 表及显式迁移。
- 创建 `src/persistence/WishlistOutboxEvent.cs`：Drasi CDC 消费的 append-only wishlist event。
- 创建 `src/services/PostgresWishlistRepository.cs`、`src/services/PostgresProfileSnapshotRepository.cs`、`src/services/PostgresRecommendationRepository.cs`、`src/services/PostgresNotificationRepository.cs`：只实现此垂直切片使用的仓储接口。
- 创建 `src/lib/AiOptions.cs`、`src/lib/ChatClientRegistration.cs`：集中配置 OpenAI-compatible 与既有 Azure `IChatClient`。
- 修改 `src/Program.cs`：保持 Azure 默认注册；在 SelfHosted 模式注册 PostgreSQL/本地模型服务且不启动 Azure hosted services。
- 修改 `src/services/DrasiDaprSubscriber.cs`、`src/services/SseStreamService.cs`：让 reaction 存储/广播错误显式失败。
- 创建 `AppHost/AppHost.csproj`、`AppHost/Program.cs`、`AppHost/Properties/launchSettings.json`：Aspire 13.6 管理 API、PostgreSQL，并把用户控制的模型配置安全传给 API。
- 创建 `tests/unit/RuntimeModeOptionsTests.cs`、`tests/unit/RuntimeServiceRegistrationTests.cs`、`tests/unit/ChatClientRegistrationTests.cs`。
- 创建 `tests/integration/PostgresPersistenceTests.cs`、`tests/integration/SelfHostedRealtimePipelineTests.cs`、`tests/integration/LocalAiAgentTests.cs`。
- 修改 `tests/Tests.csproj`、`src/src.csproj`、`SantaDigitalShowcae25.sln`、`README.md` 及 Drasi validation guide。

## Task 1：定义显式 runtime mode 并隔离 Azure 服务图

**Files:**
- 创建：`src/lib/RuntimeModeOptions.cs`
- 创建：`src/lib/RuntimeServiceRegistration.cs`
- 修改：`src/appsettings.json`
- 创建：`tests/unit/RuntimeModeOptionsTests.cs`
- 创建：`tests/unit/RuntimeServiceRegistrationTests.cs`
- 修改：`src/Program.cs`（拆分 Azure/SelfHosted 服务注册，并移除 wishlist route 中未使用的 `IEventPublisher` 参数）

**Interfaces:**

```csharp
public enum RuntimeMode
{
    Azure,
    SelfHosted
}

public sealed class RuntimeModeOptions
{
    public const string SectionName = "Runtime";
    public RuntimeMode Mode { get; init; } = RuntimeMode.Azure;
}
```

`RuntimeServiceRegistration.AddApplicationRuntime(IServiceCollection, IConfiguration)` 验证 `Runtime:Mode` 只接受 `Azure` 或 `SelfHosted`。缺省保持 `Azure`，无效值启动时报出配置键和值。Azure 与 SelfHosted 服务注册分别由 `AddAzureRuntime`、`AddSelfHostedRuntime` 执行。

- [ ] **步骤 1：写 runtime mode 红测试**

在 `RuntimeModeOptionsTests` 覆盖：`src/appsettings.json` 明确默认 `Runtime:Mode=Azure`；`SelfHosted` 能绑定；不支持的字符串在服务验证时失败并包含 `Runtime:Mode`。

- [ ] **步骤 2：运行目标测试确认失败**

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~RuntimeModeOptionsTests`

预期：测试因 `RuntimeModeOptions` 和注册入口尚不存在而失败。

- [ ] **步骤 3：实现选项和验证**

实现上方类型并通过 `OptionsBuilder.Validate` 验证模式；不要用 `Enum.TryParse` 失败后回退到 Azure。将 wishlist endpoint 中未使用的 `IEventPublisher` DI 参数移除，避免 SelfHosted request 解析 Azure Event Hubs publisher。

- [ ] **步骤 4：写 DI 隔离测试并实现分支注册**

测试 Azure 默认模式包含当前 Cosmos/Change Feed/Azure AI 注册；SelfHosted 模式包含 PostgreSQL 仓储和配置的 `IChatClient`，不注册 `CosmosClient`、`CosmosWishlistChangeFeedPublisher`、`CosmosRecommendationChangeFeedPublisher`、`DrasiHubCacheSeeder`、`ISecretProvider`、`IEventPublisher` 或 Azure OpenAI client。

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~RuntimeMode`

预期：所有模式绑定及 DI 隔离断言通过；Azure 默认注册不变。

## Task 2：建立 wishlist、outbox 及通知 PostgreSQL schema

**Files:**
- 创建：`src/persistence/AppDbContext.cs`
- 创建：`src/persistence/AppDbContextFactory.cs`
- 创建：`src/persistence/EntityConfigurations.cs`
- 创建：`src/persistence/WishlistOutboxEvent.cs`
- 创建：`src/persistence/Migrations/` 下显式 migration
- 创建：`tests/integration/PostgresPersistenceTests.cs`
- 修改：`src/src.csproj`
- 修改：`tests/Tests.csproj`
- 创建：`.config/dotnet-tools.json`（若仓库无本地 `dotnet-ef` manifest）

**Interfaces:**

- `AppDbContext` 暴露本阶段使用的 `Wishlists`、`WishlistOutboxEvents`、`ProfileSnapshots`、`Recommendations`、`Notifications`。
- `WishlistOutboxEvent` 映射到 `wishlist_events`，有 `id`、`child_id`、`text`、`type`、`dedupe_key`、`created_at` 列；事件行为是 append-only，供 Drasi PostgreSQL CDC 读取。
- 迁移保留现有领域实体字段；不把 Cosmos SDK 类型加入新的 PostgreSQL 仓储接口。

- [ ] **步骤 1：添加固定兼容版本**

在 `src/src.csproj` 添加 `Microsoft.EntityFrameworkCore` 9.0.20、`Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4；在 `tests/Tests.csproj` 添加 `Testcontainers.PostgreSql` 4.15.0。加入 `Microsoft.EntityFrameworkCore.Design` 9.0.20 和本地 `dotnet-ef` 9.0.20 tool manifest。`AppDbContextFactory` 从 `ConnectionStrings:elves` 构造 migration design-time context，不启动 WebApplication、不读取任何 Azure 配置。

- [ ] **步骤 2：先写空库迁移及持久化测试**

`PostgresPersistenceTests` 使用 PostgreSQL Testcontainer，覆盖空数据库执行迁移、wishlist/outbox/notification 插入后由新 `DbContext` 读取、outbox 写入失败时 wishlist 事务回滚、同一主键冲突由数据库拒绝。

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~PostgresPersistenceTests`

预期：实现前测试构建失败，缺少 `AppDbContext`、实体映射和迁移。

- [ ] **步骤 3：实现 EF 映射和迁移**

显式配置 `id` 主键、`child_id` 查询索引和 `created_at` 排序索引；JSON 集合按已批准数据计划映射为 `jsonb`。不在服务启动时静默创建 schema。

生成 migration：

```powershell
dotnet ef migrations add SelfHostedValidationSlice --project src\src.csproj --startup-project src\src.csproj --output-dir persistence\Migrations
```

- [ ] **步骤 4：运行数据库测试**

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~PostgresPersistenceTests`

预期：空库迁移成功、跨 DbContext round-trip 保留字段、失败事务不留下半条 wishlist/outbox 记录。

## Task 3：实现自托管仓储及原 wishlist API 的事务 outbox

**Files:**
- 创建：`src/services/PostgresWishlistRepository.cs`
- 创建：`src/services/PostgresProfileSnapshotRepository.cs`
- 创建：`src/services/PostgresRecommendationRepository.cs`
- 创建：`src/services/PostgresNotificationRepository.cs`
- 修改：`src/Program.cs`
- 创建：`tests/integration/SelfHostedWishlistRepositoryTests.cs`

**Interfaces:**

- 新仓储分别实现既有 `IWishlistRepository`、`IProfileSnapshotRepository`、`IRecommendationRepository`、`INotificationRepository`。
- `PostgresWishlistRepository.UpsertAsync(WishlistItemEntity)` 在一个 EF transaction 中写入 wishlist 领域记录和 `WishlistOutboxEvent`；event 保留 `childId`、`text`、`type`、`dedupeKey`、`createdAt`。
- Azure 模式继续注册原 Cosmos 仓储；SelfHosted 模式只将本垂直切片接口指向 PostgreSQL 实现。

- [ ] **步骤 1：写仓储与事务测试**

覆盖普通 wishlist 和 behavior-update event 字段映射、profile/recommendation/notification round-trip、写入事务失败无部分提交。测试调用公开仓储接口，不依赖 Cosmos `Container`。

- [ ] **步骤 2：运行测试确认失败**

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~SelfHostedWishlistRepositoryTests`

预期：测试因自托管仓储实现缺失而失败。

- [ ] **步骤 3：实现 PostgreSQL 仓储并注册 SelfHosted DI**

使现有 `WishlistService.AddAsync` / `AddLetterAsync` 不变，通过 `PostgresWishlistRepository` 在 primary write 成功时原子创建 outbox event。将通知历史查询按 `ChildId` 过滤并按创建时间降序。Azure registrations 保持在 Azure 分支。

- [ ] **步骤 4：运行仓储测试**

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~SelfHostedWishlistRepositoryTests`

预期：事务原子性、字段映射、跨 context 读取全部通过。

## Task 4：统一 Agent chat client，同时保留 Azure 实现

**Files:**
- 创建：`src/lib/AiOptions.cs`
- 创建：`src/lib/ChatClientRegistration.cs`
- 修改：`src/Program.cs`
- 修改：`src/services/AgUiEndpoints.cs`
- 修改：`src/services/MultiAgentOrchestrator.cs`
- 修改：`src/services/RecommendationService.cs`
- 创建：`tests/unit/ChatClientRegistrationTests.cs`
- 创建：`tests/unit/AgentProviderWiringTests.cs`
- 创建：`tests/integration/LocalAiAgentTests.cs`

**Interfaces:**

- `AiOptions` 绑定 `AI:Endpoint`、`AI:Model`、`AI:ApiKey` 并启动时验证 endpoint/model。
- SelfHosted 注册使用标准 OpenAI `ChatClient` 的 endpoint 构造并调用 `.AsIChatClient()`；Azure 模式保留当前 AzureOpenAI client 构造并包装到 `IChatClient`。
- 所有 Agent 调用点通过 DI 获取同一模式选择的 `IChatClient`；Azure NuGet package 与注册在阶段 0 完整闸门通过前保留。

- [ ] **步骤 1：添加缺配置和模式路由测试**

测试无 endpoint/model 时错误包含对应配置键；SelfHosted 模式使用 OpenAI-compatible client descriptor；Azure 模式使用已有 Azure client descriptor；API key 值不会出现在异常或日志中。

- [ ] **步骤 2：实现统一注册并替换直接构造**

将主 Agent、AG-UI、`MultiAgentOrchestrator`、`RecommendationService` 的客户端构造统一到注册入口。不得吞掉 provider exception；`OperationCanceledException` 继续传播；任何保留的 fallback 继续通过现有 `FallbackUsed` 形状明确标记。

- [ ] **步骤 3：添加模型 opt-in 集成测试**

`LocalAiAgentTests` 只在 `LOCAL_AI_TESTS=1` 时访问真实 endpoint。将 `LLM_BASE_URL`、`LLM_MODEL_NAME`、`LLM_API_KEY` 映射到 `AI:*`；通过 SelfHosted `MultiAgentOrchestrator` 要求 `CheckBudgetConstraints("Lego, book", 60)`，断言工具结果含 `Estimated Cost: $54.98` 和 `Within Budget`，再测试 Agent SSE 流有 content delta 和正常终止状态。任何测试输出、logger snapshot 和失败消息都不得包含密钥。

- [ ] **步骤 4：运行本地模型测试**

使用当前已验证的用户控制模型 endpoint：

```powershell
$env:LOCAL_AI_TESTS = '1'
dotnet test tests\Tests.csproj --filter FullyQualifiedName~LocalAiAgentTests
```

预期：项目 Agent 的真实工具调用和流式响应成功；缺少 key/model/service 时测试显式失败或跳过规则生效，不产生伪造成功输出。

## Task 5：使 Drasi reaction 持久化通知后再确认并广播

**Files:**
- 修改：`src/services/DrasiDaprSubscriber.cs`
- 修改：`src/services/SseStreamService.cs`
- 创建：`tests/integration/SelfHostedRealtimePipelineTests.cs`

- [ ] **步骤 1：写成功及错误语义测试**

使用 TestServer 和 PostgreSQL Testcontainer：连接 `GET /api/v1/notifications/stream/{childId}` 后，向 `POST /api/v1/dapr/drasi/wishlist-updates` 发送 `{ "data": { "childId": "child-1", "text": "Wind-up train" } }`。断言 handler 先持久化 notification，再返回 202，SSE 接收到 `event: notification` 且含预期 child/`Wind-up train`；新建 DbContext 仍能读到记录。再注入持久化失败，断言 handler 返回非 2xx 且没有 SSE/SignalR 广播。

- [ ] **步骤 2：验证测试初始失败**

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~SelfHostedRealtimePipelineTests`

预期：失败显示当前 Cosmos 仓储无法在 SelfHosted 模式解析，或当前 handler 把持久化错误误报为 Accepted。

- [ ] **步骤 3：修正 reaction handler 错误处理**

缺失/无效 CloudEvent `data` 返回 400；数据库不可用返回 5xx 供 Dapr retry；`OperationCanceledException` 传播；只有 notification 写入成功后才调用 `IStreamBroadcaster.PublishAsync` 并返回 202。为 query id `wishlist-updates` 映射通知类型 `wishlist`，message 由 query result 的 `text` 构造。日志记录 queryId/status，不记录 API key 或原始凭据。

- [ ] **步骤 4：让 SSE 历史读取显式失败**

从 `SseStreamService.StreamNotificationsAsync` 移除将历史读取异常吞掉后继续 stream-only 的 broad catch。存储查询失败时传播错误，不得伪装为有完整历史的成功连接。SignalR broadcast 验证使用现有 `HubStreamBroadcaster` 的 child group 和 `stream` 方法。

- [ ] **步骤 5：运行通知实时集成测试**

运行：`dotnet test tests\Tests.csproj --filter FullyQualifiedName~SelfHostedRealtimePipelineTests`

预期：事件仅在 PostgreSQL commit 后发出；失败时不发 SSE/SignalR event，Dapr 收到可重试状态。

## Task 6：添加 Aspire AppHost 与可路由的 kind 端点

**Files:**
- 创建：`AppHost/AppHost.csproj`
- 创建：`AppHost/Program.cs`
- 创建：`AppHost/Properties/launchSettings.json`
- 创建：`tests/scripts/validate-dev-network.ps1`
- 修改：`SantaDigitalShowcae25.sln`
- 修改：`drasi/local/kind.yaml`
- 修改：`.gitignore`

**Interfaces:**

- AppHost 固定使用 Aspire.Hosting.AppHost、Aspire.Hosting.PostgreSQL、Aspire.Hosting.JavaScript `13.6.0`，target `net9.0`。
- AppHost 设置 `Runtime__Mode=SelfHosted`，API 使用 PostgreSQL database reference 和 `AI__Endpoint`/`AI__Model`/`AI__ApiKey`。
- 模型 endpoint 来自 `LLM_BASE_URL`；模型名来自 `LLM_MODEL_NAME`；key 以 secret configuration 注入，不建模成容器，不写日志。
- kind 内 Drasi 使用可路由的 `DEV_CLUSTER_HOST` 访问 Aspire PostgreSQL 端口和 API HTTP 端口，禁止传入 Pod 无法访问的 `localhost`。

- [ ] **步骤 1：写 AppHost build 和网络契约测试**

添加最小 AppHost project；网络脚本接受 `-ClusterProvider kind`，验证当前 Kubernetes context 与目标集群、API host/port 和 PostgreSQL host/port，任一端点失败时打印脱敏的目标类别和 context 并返回非零。

- [ ] **步骤 2：建立 AppHost 并构建**

AppHost 创建 PostgreSQL resource + `elves` database，引用 `Projects.src`，设置 SelfHosted runtime 和 AI env mappings，wait for PostgreSQL/API；Vite 仍通过现有 JavaScript integration 运行。用 `.WithDataVolume()` 保留开发数据，固定 host port 前先检测冲突。

运行：`dotnet build AppHost\AppHost.csproj`

预期：AppHost 构建成功，Azure 模式的既有 solution 项目仍可独立构建。

- [ ] **步骤 3：探测 Windows host 与 kind Pod 双向连通**

从 kind/Drasi pod 访问 AppHost API `/healthz` 和 PostgreSQL TCP endpoint；从 Aspire API 访问 Drasi query/view endpoint。脚本不得假定 Docker Desktop 专属 host alias；只有被实测可达的地址才能写入 source/reaction manifest。

运行：`pwsh -File tests\scripts\validate-dev-network.ps1 -ClusterProvider kind`

预期：API 与 PostgreSQL 两端点都可达；失败即停止，不以 `localhost`、hostPort 猜测或跳过错误代替成功。

## Task 7：关闭完整阶段 0 闸门并更新证据

**Files:**
- 修改：`drasi/local/` 下本地 source/query/reaction overlay
- 修改：`docs/guides/drasi-self-hosted-validation.md`
- 修改：`README.md`
- 修改：`docs/superpowers/plans/2026-10-04-azure-independent-self-hosting.md`

- [ ] **步骤 1：定义生产形状的 Drasi 本地 overlay**

使用已安装且已验证的 Drasi 0.10 PostgreSQL source 和 HTTP reaction。Source 指向 `wishlist_events` 表，查询投影为 `id`、`childId`、`text`、`type`、`dedupeKey`、`createdAt`；reaction body 用 CloudEvent `data` 包装字段并 POST 至可从 Drasi Pod 访问的 API Dapr route。任何密码只通过 `drasi secret` 或未提交的本地 secret 输入。

- [ ] **步骤 2：从现有 API 写入 wishlist 并追踪完整链路**

启动 `Runtime:Mode=SelfHosted` AppHost、PostgreSQL、kind Drasi；打开 notification SSE 与 SignalR child group；调用 `POST /api/v1/children/{childId}/wishlist-items` 写唯一 item。预期 API 201 且无 fallback，PostgreSQL 同事务有 wishlist/outbox，Drasi query 出现对应字段，reaction 调用 API handler，notification 被 PostgreSQL 保存，SSE 与 SignalR 均收到相应事件。

- [ ] **步骤 3：运行完整目标测试**

运行：

```powershell
dotnet test tests\Tests.csproj --filter "FullyQualifiedName~RuntimeMode|FullyQualifiedName~PostgresPersistenceTests|FullyQualifiedName~SelfHostedWishlistRepositoryTests|FullyQualifiedName~SelfHostedRealtimePipelineTests|FullyQualifiedName~ChatClientRegistrationTests|FullyQualifiedName~AgentProviderWiringTests"
dotnet build SantaDigitalShowcae25.sln
```

随后运行 `LOCAL_AI_TESTS=1` 的 PowerShell 集成测试及 `tests\scripts\validate-dev-network.ps1`。预期所有程序化测试通过，Drasi query/event 和 API/实时输出逐项通过。

- [ ] **步骤 4：记录 gate 状态**

只有所有网络、Agent、storage、query、reaction、SSE 和 SignalR 验收通过后，才将完整 compatibility gate 记为 **Passed**。任一失败则记录精确失败边界并保持 **Blocked**；不删除任何 Azure 注册、包或部署资源。

## 执行顺序和交接

本计划是阶段 0A 增量路径，必须先于数据计划中移除 Azure runtime 的任务。此计划通过后，再按总计划执行完整数据/Drasi、AI 清理、开发启动和 Linux Kubernetes 工作流。
