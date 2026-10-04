# 脱离 Azure 的自托管改造：评估与目标设计

**日期：** 2026-10-04
**状态：** 提案；架构方向已确认，详细规格等待用户审阅

## 1. 目标与范围

让项目能在开发者电脑和自有 Linux 主机上运行，不依赖 Azure 账户、Azure 托管服务或 Azure 凭据。保留 React 前端、.NET API 和业务流程、Drasi 连续查询、实时更新以及 AI Agent/工具调用体验。

默认运行环境使用自托管组件。范围包含本地模型推理，不要求使用 Azure 或其他外部托管 AI 服务。初次部署可能需要下载容器镜像和模型权重，但正常运行时不得调用 Azure。开发机与 Linux 主机共用同一套应用及 Drasi 部署清单。开发机可通过脚本创建本地 Kubernetes 集群；Linux 部署可使用已有的 K3s 或标准 Kubernetes 集群。

本评估不包括从现有 Azure 生产环境迁移数据、多主机高可用或指定模型/硬件的性能基准。若需保留现有云端数据，应另行明确导出、导入和切换要求。

## 2. 当前项目情况

- API 将 Cosmos DB 注册为主要持久化实现。`ICosmosRepository.GetContainer` 暴露 Cosmos SDK 的 `Container`，且有五个领域仓储继承 `CosmosRepositoryBase<T>`。缺少 Cosmos 配置时，[Program.cs](../../../src/Program.cs) 会尝试通过 Key Vault 创建客户端；失败后会在启动期间抛错。因此目前无法仅靠更换连接字符串切换数据库。
- Wishlist 和 recommendation 的变更由 Cosmos DB Change Feed 读取后发布到 Event Hubs。Drasi 也配置了 Event Hubs 数据源和 Microsoft Entra 工作负载身份。Drasi 图中还有一个将结果同步到 Cosmos 状态存储的 `SyncDaprStateStore` reaction。部分 API 路径也会直接发布事件，因此迁移前必须确认唯一、规范的事件语义及去重行为。
- Azure OpenAI 客户端构造分布在主要依赖注册和其他 Agent/API 代码路径中，尚未统一经由一个可配置的模型提供者边界。
- [infra/main.bicep](../../../infra/main.bicep) 会部署 Azure OpenAI、Cosmos DB、Event Hubs、Azure Container Apps、AKS、Key Vault、ACR 和 Azure Monitor。[azure.yaml](../../../azure.yaml) 与部署脚本负责编排这些 Azure 资源。仓库中有 Drasi 的 Kubernetes 清单，但没有用于启动完整本地环境的 Docker Compose 定义。

相关实现和部署文件：

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

## 3. 备选路线

### A. 保留 Drasi 和 Agent 体验，替换 Azure 服务（推荐）

将应用持久化迁到 PostgreSQL，将模型调用收敛到一个边界并接入本地 OpenAI-compatible 推理服务，在开发机/自有 Kubernetes 上运行 Drasi、API 和前端。把 Event Hubs 输入端和 Cosmos 状态 reaction 改为 Drasi 支持的自托管集成。

此路线最符合已确认的目标并能保留主要体验，但涉及较大的数据层、事件链和部署改造。Drasi 适配器兼容性是必须通过的发布闸门，不能当作已知事实。

### B. 使用 Azure 本地模拟器作为过渡

使用本地 Cosmos DB 和 Event Hubs 模拟器、本地 Kubernetes 以及本地模型。这能减少早期 API/存储改动，可用于验证部分 Azure SDK 路径，但不能形成适合长期自托管服务器的架构：模拟器功能有限，且 Event Hubs 模拟器官方文档明确说明不可用于生产环境。可作为短期开发辅助，不能作为最终目标。

参考：[Azure Cosmos DB Linux 模拟器](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux)、[Azure Event Hubs 模拟器限制](https://learn.microsoft.com/en-us/azure/event-hubs/overview-emulator)。

### C. 用应用内投影替代 Drasi

使用自托管数据库和应用工作进程计算实时投影并通知客户端。此路线部署更简单，但会失去用户希望保留的 Drasi 连续查询体验。仅当 Drasi 自托管兼容性闸门失败、且用户明确批准行为变更时，才考虑此路线。

## 4. 推荐目标架构

```text
浏览器
  |
  v
React 前端 + ASP.NET Core API
  |                         |
  |                         +--> 本地 OpenAI-compatible 模型服务
  |                              （需支持工具调用和流式响应）
  |
  +--> PostgreSQL
         |  应用数据和持久化事件/outbox 记录
         |
         +--> Drasi 兼容的自托管数据源（需通过兼容性闸门）
                    |
                    v
                 Kubernetes 上的 Drasi
                    |             |
                    |             +--> 保持现有 SignalR/SSE 更新契约
                    +--> 自托管结果/状态 reaction（需通过兼容性闸门）
```

### 应用与数据

尽可能保持 API 路由、DTO 和业务服务契约稳定。将 Cosmos SDK 的使用移入领域级持久化接口之后，接口不得暴露 Cosmos 类型。推荐 PostgreSQL 作为持久化数据库，因为它可在开发机和 Linux 主机自托管，且具备成熟的持久卷和备份工具。

使用事务 outbox 保存应用事件，避免业务写入已提交但 Drasi 事件静默丢失。保留现有事件字段、schema 版本、幂等和去重语义。不得将现有进程内存储当作生产持久化的后备实现。

### Drasi 与事件集成

保留现有连续查询定义和客户端实时契约。必须替换当前 Event Hubs 数据源与 Cosmos Dapr-state reaction。先针对目标事件格式验证由 Drasi 项目维护的自托管数据源。若所选 Drasi 版本支持，优先验证 PostgreSQL 变更数据捕获；其次才考虑由兼容 Drasi 数据源读取的自托管消息代理。查询结果优先使用已验证的自托管 Dapr 状态组件，或能保留现有 API 行为的应用端 reaction。

如果没有受支持的数据输入和结果输出路径可保留必要查询及事件语义，则停止大规模迁移，并把兼容性发现交由用户决定：开发自定义 Drasi 集成，或改为不保留 Drasi 的方案。

### AI 与密钥

所有 Agent Framework 调用路径统一经由一个已配置的 chat-client/模型提供者边界，接入本地 OpenAI-compatible 推理端点。必须针对选定模型验证流式响应、结构化/工具调用、取消、超时和错误报告。模型推理不可用时，不得静默回退到固定文本或伪造成功响应。

本地开发使用环境配置；Linux 主机使用挂载的 secrets。Azure Key Vault 和托管身份不再是运行时要求。

### 部署与运维

使用共享 Kubernetes 基础清单（例如 Kustomize）部署 API、前端、数据库、本地模型端点及 Drasi 集成。开发者启动命令可创建本地 kind/k3d 集群；Linux 部署使用 K3s 或标准 Kubernetes 并共用这些清单。由于仓库中现有 Drasi 部署基于 Kubernetes，这一方案优先保证运行环境一致性，而不强行采用仅 Docker Compose 的设计。

提供持久卷、就绪检查、数据库初始化/迁移、备份恢复说明和可操作日志。本地启动命令必须明确报告容器运行时、集群、模型文件或配置缺失，不得在部分服务未启动时报告整体成功。Azure Bicep/azd 文件可归档或移出默认部署路径，但不得成为构建、启动、测试或运行自托管目标的前置条件。

## 5. 交付阶段与决策闸门

### 阶段 0：兼容性验证

1. 在本地 Kubernetes 集群启动选定版本的 Drasi。
2. 使用代表性 wishlist 事件，验证一条自托管输入路径和一条非 Cosmos 的结果/状态路径。
3. 运行当前关键 Drasi 查询，并验证结果形状、顺序和去重预期。
4. 验证 SignalR/SSE 更新能到达现有前端。
5. 验证本地模型端点支持应用正在使用的 Agent Framework 工具调用和流式响应。

**闸门：** 五项均通过，或用户批准并记录行为变更后，才开始大范围存储/事件迁移。

### 阶段 1：提供者边界

从业务仓储契约中移除 Cosmos 专属 SDK 类型。实现 PostgreSQL 持久化、明确的数据库 schema/迁移以及持久化集成测试。统一 chat-client 构造，并移除其他调用路径中的 Azure OpenAI 客户端直接构造。配置验证必须明确指出数据库、事件或模型设置缺失。

### 阶段 2：持久事件与 Drasi 链路

实现事务 outbox 和已选定的 Drasi 兼容数据源/reaction。运行路径移除 Cosmos Change Feed、Event Hubs、Cosmos 状态存储及 Azure 身份依赖。验证重启恢复、重试、重复投递和错误传递。

### 阶段 3：自托管打包

增加共享 Kubernetes 清单、本地集群引导、Linux 部署说明、密钥和数据卷配置、就绪检查、备份恢复步骤以及完整本地集成测试配置。更新根目录和服务文档，将自托管部署作为默认路径。

## 6. 验收标准

1. 全新开发环境只需安装文档列出的容器和本地 Kubernetes 前置条件，即可通过一条文档化命令启动完整服务；不需要 Azure 登录、Azure CLI、azd、Azure 端点变量或 Azure 资源。
2. Linux 主机可将相同的应用和 Drasi 清单部署到 K3s 或标准 Kubernetes，不增加 Azure 托管服务。
3. API 写入的数据保存在 PostgreSQL，并在 API/数据库 Pod 重启后仍存在（持久卷按配置工作）。
4. 一条代表性 wishlist 写入会被可靠记录，经 Drasi 处理并反映在相关连续查询结果中，随后通过现有实时 API 契约送达客户端。
5. AI Agent 能使用配置的本地模型完成流式响应及所需工具调用。模型未启动或不可用时，健康检查或请求返回明确错误。
6. 测试覆盖持久化、事件/outbox 行为、Drasi 查询/结果契约、客户端流式更新和本地模型集成。没有测试将进程内存状态误当作持久化存储。
7. 自托管运行时不需要 Azure SDK 凭据、Azure 服务端点、Azure 云资源部署，也不存在隐藏的 Azure 网络调用。历史 Azure 文档可以保留，但必须标为旧路径。
8. 启动、就绪状态、日志及部署脚本能明确指出缺少的依赖或启动失败的组件；必要组件不可用时不得报告整体部署成功。

## 7. 主要风险与控制措施

| 风险 | 影响 | 控制措施 |
|---|---|---|
| Drasi 版本缺少受维护的自托管数据源或结果适配器 | 阻塞推荐架构或需要自定义集成 | 将兼容性设为阶段 0 闸门；在修改仓储前验证数据源和 reaction |
| Cosmos 类型及 Change Feed 模式渗入持久化和事件发布 | 改造范围大且可能造成行为偏差 | 先建立与提供者无关的契约，再用 API/事件契约测试保护行为 |
| 当前多个事件发布路径可能产生重叠事件 | Drasi 结果重复或顺序变化 | 替换发布器之前明确规范的写入到事件链路及去重要求 |
| 本地模型的性能与工具调用能力随模型和硬件不同 | Agent 功能退化或延迟过高 | 固定经过测试的模型/运行时配置，并在目标硬件上运行工具调用/流式集成测试 |
| 本地 Kubernetes 和模型文件占用较多系统资源 | 开发环境比当前仅启动 API 更复杂 | 发布最低资源建议、就绪检查，以及镜像/模型下载步骤 |
| 现有 Azure 部署脚本和文档让用户以为 Azure 是强制条件 | 用户继续采用旧部署路径 | 将自托管快速入门设为主路径，并将 Azure 内容标记为可选旧路径 |

## 8. 评估结论

项目可以改造成不依赖 Azure 的自托管应用，但这属于高复杂度现代化改造，不是替换部署模板即可完成。最重要的工作是替换 Cosmos 专属持久化和 Change Feed 链路，同时保留 Drasi 行为。建议先验证 Drasi 兼容性，再重构存储和 AI 提供者，最后统一开发机与 Linux 主机的 Kubernetes 部署。本地模拟器可以帮助缩短部分开发反馈周期，但不满足长期自托管服务器目标。
