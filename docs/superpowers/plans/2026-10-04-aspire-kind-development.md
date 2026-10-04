# Aspire 13.6 与 kind/k3d 开发启动实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让开发者通过一条 PowerShell 命令创建或复用本地 kind/k3d 集群、启动 Drasi，并用 .NET Aspire 13.6 编排 API、PostgreSQL、本地模型和前端开发服务器。

**Architecture:** Aspire AppHost 管理应用侧进程/容器；独立 bootstrap 脚本负责 Kubernetes context 和 Drasi/Dapr 安装。脚本先确保集群和 Drasi operator 就绪，再启动 AppHost 并等待 PostgreSQL/API/模型就绪，随后应用 provider/query/reaction manifests 并等待其 readiness。kind/k3d 与 Aspire 容器网络间使用显式可路由地址，禁止向 Pod 传入不可达的 `localhost`。

**Tech Stack:** .NET 9、Aspire.Hosting.AppHost 13.6.0、Aspire.Hosting.PostgreSQL 13.6.0、Aspire.Hosting.JavaScript 13.6.0、PowerShell 7、Docker、kind/k3d、Drasi/Dapr。

## Global Constraints

- Aspire 版本固定为 13.6.0；API 保持 `net9.0`。
- Aspire 只负责编排开发机上的 API、PostgreSQL、本地模型及前端；不负责创建 Kubernetes 集群或 Linux 生产部署。
- 启动脚本创建集群前验证当前 context；已存在集群时复用，不自动删除集群、PVC 或数据库数据。
- 使用数据/AI 计划确定的 `ConnectionStrings:elves`、`AI:Endpoint`、`AI:Model` 和 Drasi endpoint 配置契约。
- 若 kind/k3d Pod 无法访问 Aspire 管理的数据库/模型或反向访问 Drasi，必须先修网络，不能以 Docker Desktop 专属猜测地址绕过测试。

---

## 文件结构与边界

- 创建 `AppHost/AppHost.csproj`、`AppHost/Program.cs`、`AppHost/Properties/launchSettings.json`：声明本地服务图。
- 创建 `drasi/local/kind.yaml`、`drasi/local/k3d.yaml`：只定义本地集群端口映射和节点资源，不含凭据。
- 创建 `scripts/dev-up.ps1`、`scripts/dev-down.ps1`：校验工具、创建/复用集群、安装 Drasi/Dapr、启动/停止 AppHost。
- 修改 `SantaDigitalShowcae25.sln`：加入 AppHost 项目。
- 修改 `.gitignore`：忽略本地 pid/log、模型数据和未加密 secrets。
- 修改 `README.md`：提供唯一开发启动命令、前置条件及故障处理。

## Task 1：建立 Aspire 13.6 AppHost 服务图

**Files:**
- 创建：`AppHost/AppHost.csproj`
- 创建：`AppHost/Program.cs`
- 修改：`SantaDigitalShowcae25.sln`
- 修改：`.gitignore`

**Interfaces:**
- AppHost TFM 设为 `net9.0`，引用 `src/src.csproj`；solution 内项目标识为 `src`，故生成引用类型为 `Projects.src`。
- AppHost 使用 package `Aspire.Hosting.AppHost`、`Aspire.Hosting.PostgreSQL`、`Aspire.Hosting.JavaScript`，版本均为 `13.6.0`。
- AppHost 生成给 API 的 `ConnectionStrings:elves`，并注入 `AI:Endpoint`、`AI:Model`、Drasi base URL。

- [ ] **步骤 1：写 AppHost wiring smoke test/验证入口**

创建 `AppHost` 项目后运行 `dotnet build AppHost\AppHost.csproj`；将此作为后续 AppHost wiring 的红/绿验证入口。确认当前 solution 的两个既有项目仍能独立编译。

- [ ] **步骤 2：建立最小 AppHost**

AppHost 定义 PostgreSQL database 资源、API project、Vite frontend 和可配置的本地模型容器。关键资源声明使用以下 Aspire 13.6 API 形式：

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();
var database = postgres.AddDatabase("elves");
var modelImage = builder.Configuration["AI:ContainerImage"]
    ?? throw new InvalidOperationException("AI:ContainerImage is required.");
var modelTag = builder.Configuration["AI:ContainerTag"]
    ?? throw new InvalidOperationException("AI:ContainerTag is required.");
var modelPort = int.Parse(builder.Configuration["AI:ContainerPort"]
    ?? throw new InvalidOperationException("AI:ContainerPort is required."));
var model = builder.AddContainer("model", modelImage, modelTag)
    .WithHttpEndpoint(name: "http", targetPort: modelPort);
var api = builder.AddProject<Projects.src>("api")
    .WithHttpEndpoint(port: 8080)
    .WithReference(database)
    .WithReference(model)
    .WithEnvironment("AI__Endpoint",
        ReferenceExpression.Create($"{model.GetEndpoint("http")}/v1"))
    .WithEnvironment("AI__Model", builder.Configuration["AI:Model"]
        ?? throw new InvalidOperationException("AI:Model is required."))
    .WaitFor(database)
    .WaitFor(model);
builder.AddViteApp("frontend", "../frontend", "dev")
    .WithReference(api);
builder.Build().Run();
```

将模型 image/tag/port/model 作为 AppHost 配置传给 API；`AI:Endpoint` 由 Aspire 的模型 HTTP endpoint reference 加上 `/v1` 路径生成，不允许配置不可达的 `localhost` 地址；模型密钥通过未提交的本地 secret/config 注入。为 PostgreSQL 配置可复用数据卷，例如对 PostgreSQL resource 调用 `.WithDataVolume()`。API 使用 `WaitFor` 等待数据库和模型服务就绪。所选模型运行时在 AI 计划里通过真实 tool-call 测试后再固定。

- [ ] **步骤 3：构建并确认 dashboard 资源**

运行 `dotnet build AppHost\AppHost.csproj`，预期成功并输出 AppHost 项目。运行 `dotnet run --project AppHost\AppHost.csproj`，预期 Aspire dashboard 显示 API、PostgreSQL、frontend 和模型资源的真实状态；关闭此手工进程后继续。

## Task 2：创建可路由的本地 Kubernetes 网络配置

**Files:**
- 创建：`drasi/local/kind.yaml`
- 创建：`drasi/local/k3d.yaml`
- 修改：`drasi/resources` 中经数据/Drasi计划验证过的本地 overlay
- 创建：`tests/scripts/validate-dev-network.ps1`
- 创建：`tests/scripts/DevUp.Tests.ps1`

- [ ] **步骤 1：定义 kind/k3d 本地端口映射**

kind control-plane 映射 Drasi HTTP/SSE/SignalR 所需端口；k3d 通过 server load balancer 映射相同宿主机端口。只暴露本机地址，不暴露数据库/模型端点到 LAN。

- [ ] **步骤 2：验证 Pod 到 Aspire 端点和 API 到 Drasi 端点**

从 Drasi Pod 内检查 PostgreSQL/事件 source 地址可解析且可连接；从 Aspire API 调用 Drasi health/view endpoint。`tests/scripts/validate-dev-network.ps1` 对每个端点执行带 timeout 的连接检查，失败时输出端点和当前 Kubernetes context。再经 API 写入一条 wishlist，验证已记录的 Drasi query 输出和现有 SSE/SignalR 事件字段；结合本地 AI 计划的工具调用测试，作为完整兼容性闸门的运行证据。

- [ ] **步骤 3：运行网络 smoke test**

运行 `pwsh -File tests\scripts\validate-dev-network.ps1 -ClusterProvider kind`，并在 k3d 配置存在时再运行 `-ClusterProvider k3d`。预期 Pod 到 PostgreSQL/事件 source、API 到 Drasi 的连接和 wishlist→Drasi query→SSE/SignalR 检查均成功；任何 endpoint 使用 `localhost` 且从相应容器/Pod 不可达时测试必须失败。

## Task 3：实现幂等开发机 bootstrap 命令

**Files:**
- 创建：`scripts/dev-up.ps1`
- 创建：`scripts/dev-down.ps1`
- 修改：`scripts/dev-up.ps1` 使用经验证的 `drasi/install-drasi.ps1` 流程
- 修改：`README.md`

**Interfaces:**
- 命令：`pwsh -File scripts/dev-up.ps1 -ClusterProvider kind`；`-ClusterProvider` 只接受 `kind` 或 `k3d`。
- `dev-up.ps1` 失败时返回非零 exit code；`dev-down.ps1` 只停止此脚本启动的 AppHost，不删除集群和 volume。

- [ ] **步骤 1：先写脚本行为验证**

为 `scripts/dev-up.ps1` 加 Pester 测试，覆盖：缺少 Docker/kubectl/provider binary 报具体缺失项；不受管理的当前 context 不执行 `kubectl apply`；目标集群已存在时不调用 create/delete；任何 native 命令非零退出时 wrapper 失败。README 明确列出 PowerShell 7、Docker、kubectl、kind/k3d、Drasi CLI、.NET 9 SDK 和 Node.js 运行时为前置条件。

- [ ] **步骤 2：运行 Pester 确认失败**

运行 `Invoke-Pester tests\scripts\DevUp.Tests.ps1 -Output Detailed`，预期因脚本不存在而失败。

- [ ] **步骤 3：实现安全的启动顺序**

顺序为：检查 PowerShell/Docker/kubectl/所选 kind/k3d/Drasi CLI/Aspire SDK；仅在目标 context 不存在时创建集群；切换并再次验证 context 名；安装/等待 Dapr 和 Drasi operator；启动 AppHost 并保存本次进程 PID；等待 PostgreSQL、API 和模型服务就绪；应用本地 providers/resources；检查 source、queries、reaction readiness。任何阶段失败立即退出，不运行后续阶段；不得在 PostgreSQL 尚未启动时等待其 Drasi source readiness。

- [ ] **步骤 4：实现停止命令和说明**

`dev-down.ps1` 只按本次运行生成的 pid 停止对应 AppHost process；不得按进程名杀进程、删除 Kubernetes context、集群或 PVC。README 记录两条命令、端口、模型首次下载和如何手工清理用户明确指定的测试集群。

- [ ] **步骤 5：运行脚本测试及端到端 smoke**

运行 `Invoke-Pester tests\scripts\DevUp.Tests.ps1 -Output Detailed`。在 Windows Docker 环境运行 `pwsh -File scripts\dev-up.ps1 -ClusterProvider kind`；预期部署后 `kubectl get pods -n drasi-system` 均 Ready，Aspire dashboard 显示 API/database/model/frontend healthy，`Invoke-RestMethod http://localhost:8080/healthz` 返回 HTTP 200。停止时运行 `pwsh -File scripts\dev-down.ps1` 并确认 kind cluster 仍存在。

## Task 4：提交开发启动工作流

- [ ] 运行 `dotnet build AppHost\AppHost.csproj` 和 `Invoke-Pester tests\scripts\DevUp.Tests.ps1 -Output Detailed`。
- [ ] 使用提交说明 `feat: add Aspire and local Drasi developer startup`，只提交 AppHost、local cluster config、脚本和 README。
