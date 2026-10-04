# 自托管数据与 Drasi 实时链路实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 PostgreSQL 持久化应用数据，并以经过验证的自托管事件路径替换 Cosmos DB、Change Feed、Event Hubs 及 Cosmos 状态 reaction。

**Architecture:** 保留现有仓储接口和 API 语义，在其下实现 Npgsql/EF Core PostgreSQL 仓储。业务写入与 outbox 事件同事务提交；Drasi 从阶段 0 验证通过的自托管数据源读取 outbox，查询结果经自托管 reaction 保持当前 SSE/SignalR 行为。

**Tech Stack:** .NET 9、EF Core 9、Npgsql、PostgreSQL、Drasi 0.10 系列（仅使用阶段 0 已验证的发行物）、Dapr。

## Global Constraints

- 运行路径不依赖 Azure Cosmos DB、Event Hubs、Key Vault、Azure 身份或 Azure 端点。
- 不改动现有公开 API DTO、事件字段、schema 版本、幂等及去重语义，除非契约测试明确记录用户批准的变化。
- Drasi 自托管输入和结果输出、Aspire/kind 网络、代表性查询/实时流及本地 AI 工具调用全部验证通过前，不移除现有可运行路径。
- 数据库迁移通过显式迁移文件管理；服务启动不得静默创建缺失 schema 并将失败报告为成功。
- 真正临时的 stream broadcaster、metrics、stream resume 和 Agent 取消状态可保留在内存；儿童、wishlist、recommendation、assessment、notification、job、report 和 workshop event 等业务状态不得以进程内存作为持久化替代。

---

## 文件结构与边界

- 修改 `src/services/*Repository.cs`：保留现有领域仓储接口，改为 PostgreSQL 实现。
- 新建 `src/persistence/AppDbContext.cs`、`src/persistence/EntityConfigurations.cs`、`src/persistence/Migrations/`：只负责 PostgreSQL schema、映射及迁移。
- 修改 `src/services/CosmosChangeFeedService.cs`、`src/services/EventHubPublisher.cs`、`src/services/ChildrenApi.cs`、`src/Program.cs`：移除 Cosmos Change Feed、Event Hubs 实现与路由中的直接 Cosmos 操作，注册 outbox 和新仓储。
- 修改 `src/services/JobsApi.cs`、`src/services/WishlistService.cs`、`src/services/RecommendationService.cs`、`src/appsettings.json`：将事件写入与业务事务对齐，并删除旧云端端点配置。
- 删除 `src/lib/CosmosSetup.cs`、`src/lib/KeyVaultSecretProvider.cs`、`src/lib/ISecretProvider.cs`；修改 `src/Directory.Packages.props` 和 `src/src.csproj` 移除不再使用的 Azure packages。
- 修改 `drasi/resources/providers.yaml`、`drasi/resources/drasi-resources.yaml`、`drasi/resources/cosmos-state-component.yaml` 及 `drasi/sources/eventhub-source.yaml`：只在阶段 0 验证通过后接入选定的自托管 provider/reaction。
- 修改 `tests/Tests.csproj`；以 PostgreSQL integration tests 替代 `tests/unit/CosmosRepositoryBaseTests.cs`。

## Task 1：验证 Drasi 自托管适配器（发布闸门）

**Files:**
- 检查：`drasi/resources/providers.yaml`
- 检查：`drasi/resources/drasi-resources.yaml`
- 检查：`drasi/install-drasi.ps1`
- 检查：`drasi/manifests/kubernetes-resources.yaml`
- 创建：`docs/guides/drasi-self-hosted-validation.md`

**Interfaces:**
- 产出：一个实际版本/镜像标签、可由 Drasi 消费的自托管输入类型，以及可将结果送入自托管状态或应用端的 reaction 类型。
- 下游只使用该验证文档记录的 provider 类型、端点格式、事件 schema 和镜像标签。

- [ ] **步骤 1：确认 provider 能力和版本**

检查 Drasi 0.10 对应的官方 source/reaction provider 清单；对照仓库当前的 `azure-linux` providers。只将明确支持自托管输入和结果输出、且可在本地 Kubernetes 下载运行的 provider 作为候选。

- [ ] **步骤 2：在 kind 上安装 Drasi 并验证候选 provider**

使用现有 `drasi/install-drasi.ps1` 前，先确认脚本不会创建 Azure 资源或覆盖当前 Kubernetes context。建立名为 `santa-local` 的一次性 kind 集群，将 context 明确切换到该集群，再安装 Dapr 和 Drasi。向候选输入写入一条具有 `childId`、`text`、`type`、`dedupeKey`、`createdAt` 的事件。

运行 `drasi list source` 和 `drasi list query`；预期候选 source 显示可用，`wishlist-updates` 进入 Running，查询结果保留现有字段和值。再验证至少一个结果通过本地 reaction 到达 API/实时流。

- [ ] **步骤 3：记录兼容性结论和停止条件**

在 `docs/guides/drasi-self-hosted-validation.md` 记录 Drasi/Dapr 版本、镜像标签、source/reaction 类型、kind 网络地址、实际事件输入命令、查询结果和 SSE/SignalR 结果。此 Task 验证 source/reaction 候选和事件契约；完整发布闸门还需通过本地 AI 集成测试以及 Aspire/kind 网络计划中的 Pod 双向连通、代表性查询和实时流测试。任何 source/reaction 无法运行、Entra 身份仍被要求或事件 schema 不兼容时，将结论写为 **Blocked** 并停止 Task 2 之后的事件链任务；请求用户在自定义 Drasi adapter 与替换 Drasi 两者间决策。

## Task 2：建立 PostgreSQL schema 和迁移

**Files:**
- 修改：`src/src.csproj`
- 创建：`src/persistence/AppDbContext.cs`
- 创建：`src/persistence/EntityConfigurations.cs`
- 创建：`src/persistence/Migrations/` 下首个 EF migration
- 修改：`tests/Tests.csproj`
- 创建：`tests/integration/PostgresPersistenceTests.cs`

**Interfaces:**
- 产生 `AppDbContext : DbContext`，提供 `DbSet`：`Children`、`Wishlists`、`Recommendations`、`Profiles`、`Assessments`、`Notifications`、`Jobs`、`Reports`、`Events` 和 `OutboxMessages`。
- 每个按 child 查询的业务记录保留 `ChildId`；使用 `(ChildId, Id)` 复合主键或等价唯一约束，避免 Cosmos 分区键语义丢失。
- 在 EF Core 9/Npgsql 9 兼容范围内选定并固定 package patch 版本，API 仍目标 `net9.0`。

- [ ] **步骤 1：先写 schema/migration 集成测试**

在 `tests/Tests.csproj` 加入 `Testcontainers.PostgreSql` 4.x 测试依赖。在 `PostgresPersistenceTests` 使用专用 PostgreSQL 测试容器，测试 `Database.MigrateAsync()` 可从空库创建 schema；插入、重启容器后读取一条 wishlist 记录；重复主键被数据库约束拒绝。

示例测试断言：

```csharp
await context.Database.MigrateAsync();
var repository = new WishlistRepository(context);
await repository.UpsertAsync(new WishlistItemEntity
{
    ChildId = "child-1",
    Text = "Lego"
});
var items = new List<WishlistItemEntity>();
await foreach (var item in repository.ListAsync("child-1"))
    items.Add(item);
Assert.Contains(items, item => item.Text == "Lego");
```

- [ ] **步骤 2：运行目标测试确认尚未满足**

运行 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~PostgresPersistenceTests`。预期因 `AppDbContext`、迁移和 PostgreSQL 仓储尚未实现而失败。

- [ ] **步骤 3：添加 EF Core/Npgsql 映射和迁移**

在 `AppDbContext` 显式配置表名、主键、child/time 查询索引及 recommendation/assessment 的集合字段映射。对 `Recommendations.Items`、`Assessments.Items` 采用 PostgreSQL `jsonb` 映射，并增加 round-trip 测试，保证子项字段和值不变。生成首个 EF migration；启动时使用 `Database.MigrateAsync` 只能在 Development/Test profile 执行，生产迁移使用后续部署计划的显式迁移步骤。

- [ ] **步骤 4：重跑 PostgreSQL 测试**

运行上述 `dotnet test` 命令。预期 migration 可从空库成功执行、主键约束有效、集合对象能从 `jsonb` 读取还原。

## Task 3：迁移 Cosmos 领域仓储并覆盖内存业务仓储

**Files:**
- 修改：`src/services/WishlistRepository.cs`
- 修改：`src/services/RecommendationRepository.cs`
- 修改：`src/services/ProfileSnapshotRepository.cs`
- 修改：`src/services/LogisticsAssessmentRepository.cs`
- 修改：`src/services/NotificationRepository.cs`
- 修改：`src/services/ChildrenApi.cs`
- 修改：`src/services/ChildrenApi.cs` 中的 `ChildRepository`
- 修改：`src/services/EventRepository.cs`
- 修改：`src/services/JobRepository.cs`
- 修改：`src/services/ReportRepository.cs`
- 删除：`src/services/CosmosRepository.cs`
- 删除：`src/services/CosmosRepositoryBase.cs`
- 删除：`src/lib/CosmosSetup.cs`
- 修改：`src/Program.cs`
- 删除或替换：`tests/unit/CosmosRepositoryBaseTests.cs`
- 创建：`tests/integration/PostgresRepositoryTests.cs`

**Interfaces:**
- 现有接口 `IWishlistRepository`、`IRecommendationRepository`、`IProfileSnapshotRepository`、`ILogisticsAssessmentRepository`、`INotificationRepository`、`IChildRepository`、`IEventRepository`、`IJobRepository`、`IReportRepository` 的业务方法保持稳定。
- 所有仓储改为注入 `AppDbContext`；业务 API 不再通过 `IServiceProvider` 查找 `CosmosSetup`。
- **前置闸门：** Drasi source/reaction 候选、AI 流式工具调用、Aspire/kind 双向网络、代表性 Drasi 查询及 SSE/SignalR 输出均已通过并记录；否则不得删除 Cosmos/Event Hubs 运行路径。

- [ ] **步骤 1：为现有仓储接口写 PostgreSQL 行为测试**

覆盖 wishlist upsert/list、recommendation get/list 和 audit、profile snapshot get/store、assessment list/history、notification list、child add/exists、job get/upsert、report get/upsert、event dedupe lookup。测试 child 之间隔离、时间倒序及 `take` 上限。

- [ ] **步骤 2：运行测试确认缺少 PostgreSQL 实现**

运行 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~PostgresRepositoryTests`。预期失败于旧 Cosmos 构造函数或缺少测试行为。

- [ ] **步骤 3：实现仓储并移除 API 隐式 Cosmos 写入**

实现仓储查询为 EF LINQ。将 `ChildrenApi` 的儿童与 wishlist 数据写入改为明确领域仓储调用；删去其中 `CosmosSetup`、Key Vault 获取及 catch-and-ignore 代码。将 Child/Event/Job/Report 业务状态从进程内存存储迁入 PostgreSQL；stream metrics、broadcaster、resume buffer、活动 Agent cancellation 保留为临时内存状态。清除 `appsettings.json` 的 Cosmos/Key Vault 配置和硬编码 Drasi Azure gateway 地址，改为可由本地/Linux 配置覆盖的 Drasi service URLs。

- [ ] **步骤 4：移除 Cosmos 依赖注册及测试**

从 `Program.cs` 删除 `CosmosClient`、`ICosmosRepository`、`CosmosSetup` 与 Cosmos Change Feed DI；移除 Azure Cosmos/Key Vault package references 仅在全仓代码已无使用时进行。更新 readiness 和 diagnostics，使 PostgreSQL 不可用时明确失败。

- [ ] **步骤 5：运行测试并检查构建**

运行 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~PostgresRepositoryTests` 和 `dotnet build SantaDigitalShowcae25.sln`。预期仓储行为通过、API target 仍为 net9.0、编译无 Cosmos SDK 引用。

## Task 4：事务 outbox 与 Drasi 自托管反应链

**Files:**
- 修改：`src/persistence/AppDbContext.cs`
- 创建：`src/models/OutboxMessage.cs`
- 创建：`src/services/PostgresEventPublisher.cs`
- 创建：`src/services/OutboxDispatchService.cs`（仅当 Task 1 验证的 Drasi source 要求独立消息代理）
- 修改：`src/services/CosmosChangeFeedService.cs`
- 修改：`src/services/EventHubPublisher.cs`
- 修改：`src/services/ChildrenApi.cs`
- 修改：`src/services/JobsApi.cs`
- 修改：`src/Program.cs`
- 修改：`drasi/resources/providers.yaml`
- 修改：`drasi/resources/drasi-resources.yaml`
- 替换：`drasi/resources/cosmos-state-component.yaml`
- 替换：`drasi/sources/eventhub-source.yaml`
- 创建：`tests/integration/DrasiSelfHostedFlowTests.cs`

**Interfaces:**
- `IEventPublisher` 保留 `PublishWishlistAsync`、`PublishRecommendationAsync` 契约；实现必须将事件持久保存后才返回成功。
- Outbox 至少包含唯一事件 ID、child ID、事件类型、schema 版本、发生时间、去重键和 JSON payload。

- [ ] **步骤 1：先写 outbox 原子性及幂等测试**

增加测试：业务写入与 outbox 在同一数据库事务中提交；唯一去重键只生成一个规范事件；数据库失败时 API 不返回 accepted；outbox consumer 重启后仍能读取未处理事件。

- [ ] **步骤 2：运行测试确认失败**

运行 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~Outbox`。预期失败于 outbox entity、唯一约束及事务写入未实现。

- [ ] **步骤 3：实现事务 outbox**

在业务写入事务中插入 outbox 行；按 Task 1 的实际结论连接已验证的 Drasi 数据源。若 Drasi 直接读取 PostgreSQL outbox，不额外引入 broker；若必须使用另一个受支持的自托管 source，增加持久 dispatch 状态、重试和显式失败日志，不允许 `IEventPublisher` 空操作返回成功。

- [ ] **步骤 4：以验证通过的 provider 替换 Cosmos/Event Hubs reaction**

保留 `drasi/resources/drasi-resources.yaml` 中查询名、Cypher 查询及 payload 字段；替换 source 身份和 `wishlist-sync-cosmos` reaction。provider/image 标签必须与 Task 1 记录一致。删除 Cosmos state component 与 Azure Event Hubs Source 文件中的运行时身份/端点配置。

- [ ] **步骤 5：运行 end-to-end 验收**

启动 PostgreSQL、Drasi 和 API；POST 一条测试 wishlist，验证 PostgreSQL 有业务行与 outbox 行、Drasi 关键 query 产生结果、现有 SSE/SignalR 订阅收到对应字段。重新启动 outbox/API 后验证未处理事件不丢失。运行 `dotnet test tests\Tests.csproj`。

## Task 5：提交数据与 Drasi 工作流

- [ ] 先运行 `dotnet build SantaDigitalShowcae25.sln` 和 `dotnet test tests\Tests.csproj`，确认前述任务无未解决失败。
- [ ] 使用提交说明 `feat: migrate persistence and drasi events to self-hosted services`，仅提交本计划涉及的数据、测试、Drasi 文件。
