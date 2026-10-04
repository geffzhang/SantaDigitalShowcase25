# 脱离 Azure 的自托管改造：评估与目标设计

**日期：** 2026-10-04
**状态：** 原设计、PostgreSQL 目标及 SelfHosted 阶段 0A 方案均已获用户批准并复核

## 1. 目标与范围

让项目能在开发者电脑和自有 Linux 主机上运行，不依赖 Azure 账户、Azure 托管服务或 Azure 凭据。保留 React 前端、.NET API 和业务流程、Drasi 连续查询、实时更新以及 AI Agent/工具调用体验。开发机使用 .NET Aspire 13.6 AppHost 编排应用侧服务；一条启动命令按需创建 kind/k3d 集群、部署 Drasi，再启动 Aspire。Linux 主机使用 K3s 或标准 Kubernetes 部署自托管服务。

最终运行环境使用自托管组件。模型可运行于开发机或用户控制的远端主机；不要求使用 Azure 或第三方托管 AI 服务。初次部署可能需要下载容器镜像和模型权重，但正常运行时不得调用 Azure。开发机由 Aspire AppHost 定义本地服务图，Linux 主机使用 Kubernetes 部署清单；两种环境共用应用配置契约、镜像/业务代码和 Drasi 资源定义，但不要求采用相同的应用编排器。

本评估不包括从现有 Azure 生产环境迁移数据、多主机高可用或指定模型/硬件的性能基准。若需保留现有云端数据，应另行明确导出、导入和切换要求。

## 2. 当前项目情况

- API 将 Cosmos DB 注册为主要持久化实现。`ICosmosRepository.GetContainer` 暴露 Cosmos SDK 的 `Container`，且有五个领域仓储继承 `CosmosRepositoryBase<T>`。缺少 Cosmos 配置时，[Program.cs](../../../src/Program.cs) 会尝试通过 Key Vault 创建客户端；失败后会在启动期间抛错。因此目前无法仅靠更换连接字符串切换数据库。
- Wishlist 和 recommendation 的变更由 Cosmos DB Change Feed 读取后发布到 Event Hubs。Drasi 也配置了 Event Hubs 数据源和 Microsoft Entra 工作负载身份。Drasi 图中还有一个将结果同步到 Cosmos 状态存储的 `SyncDaprStateStore` reaction。部分 API 路径也会直接发布事件，因此迁移前必须确认唯一、规范的事件语义及去重行为。
- Azure OpenAI 客户端构造分布在主要依赖注册和其他 Agent/API 代码路径中，尚未统一经由一个可配置的模型提供者边界。
- [infra/main.bicep](../../../infra/main.bicep) 会部署 Azure OpenAI、Cosmos DB、Event Hubs、Azure Container Apps、AKS、Key Vault、ACR 和 Azure Monitor。[azure.yaml](../../../azure.yaml) 与部署脚本负责编排这些 Azure 资源。仓库中有 Drasi 的 Kubernetes 清单，但没有用于启动完整本地环境的 Docker Compose 定义。
- 当前仓库没有 Aspire AppHost 项目或 Aspire 引用。Aspire 13.6 是新增的开发编排层；kind/k3d 集群创建、Dapr/Drasi 部署仍由外部启动脚本负责，Aspire 本身不负责创建 Kubernetes 集群。

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
开发机启动脚本
  |-- 创建或复用 kind/k3d 集群 --> 在集群中安装 Dapr 和 Drasi
  |                                  ^             |
  |                                  |             +--> SignalR/SSE 实时更新
  |                                  |             +--> 自托管 reaction（需验证）
  |                                  |
  |                                  +--> 通过可路由地址访问开发机服务
  |
  +-- Aspire 13.6 AppHost
        |-- React 前端（如独立开发服务器）
        |-- ASP.NET Core API
        |     |-- PostgreSQL
        |     +-- OpenAI-compatible 自托管模型端点（本机或用户控制的远端主机）
        +-- 提供本地服务发现、连接配置和运行状态

Linux 主机：K3s/标准 Kubernetes 部署 API、前端、PostgreSQL、模型服务和 Drasi
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

开发机采用两层编排。`Aspire.Hosting.AppHost` 13.6.0 定义并启动 API、PostgreSQL，以及需要独立开发服务器时的前端；API 通过配置连接开发机或用户控制远端的自托管模型端点。启动脚本检查前置条件、创建或复用本地 kind/k3d 集群、安装/等待 Dapr 和 Drasi 就绪，然后启动 Aspire AppHost。脚本不得默认删除已有集群或其数据。Aspire 的 AppHost 用于开发机，不作为 Linux 生产编排器。

Aspire 服务与 Drasi Pod 位于不同网络边界。PostgreSQL、模型端点及 Drasi view/reaction 端点必须通过 kind/k3d Pod 可访问的地址配置，不能把仅对 AppHost 容器可见的 `localhost` 地址交给集群内服务。阶段 0 必须验证开发机到 Drasi 以及 Drasi 到其事件源/结果端点的双向连接。

Linux 主机用共享 Kubernetes 基础清单（例如 Kustomize）部署 API、前端、数据库、本地模型端点及 Drasi 集成。开发机和 Linux 部署共享应用配置契约和 Drasi 资源定义，但 Aspire 本地运行图与 Linux Kubernetes 清单可以不同。提供持久卷、就绪检查、数据库初始化/迁移、备份恢复说明和可操作日志。

本地启动命令必须明确报告容器运行时、Aspire 13.6、kind/k3d、Dapr/Drasi、模型文件或配置缺失，不得在部分服务未启动时报告整体成功。Azure Bicep/azd 文件可归档或移出默认部署路径，但不得成为构建、启动、测试或运行自托管目标的前置条件。

参考：[Aspire AppHost 本地编排](https://aspire.dev/get-started/app-host/)、[Aspire.Hosting.AppHost 13.6.0](https://www.nuget.org/packages/Aspire.Hosting.AppHost/13.6.0)。

## 5. 交付阶段与决策闸门

### 阶段 0：兼容性验证

1. 在 kind/k3d 集群启动选定版本的 Drasi，并确认 Dapr 及 Drasi 工作负载就绪。
2. 从 Aspire 13.6 AppHost 启动 API、PostgreSQL 以及前端开发服务器（若独立运行）；模型可由本机或用户控制的远端自托管端点提供。
3. 验证 Aspire 服务与 Drasi Pod 之间的网络寻址和连通性。
4. 使用代表性 wishlist 事件，验证一条自托管输入路径和一条非 Cosmos 的结果/状态路径。
5. 运行当前关键 Drasi 查询，并验证结果形状、顺序和去重预期。
6. 验证 SignalR/SSE 更新能到达现有前端。
7. 验证自托管模型端点支持应用正在使用的 Agent Framework 工具调用和流式响应。

**闸门：** 七项均通过，或用户批准并记录行为变更后，才开始大范围存储/事件迁移。

### 阶段 0A：并行自托管验证模式（用户批准）

阶段 0 的端到端 API/实时链路需要一个可运行的自托管应用路径。为避免“先通过完整闸门才能实现闸门所需运行路径”的顺序循环，在迁移期间增加显式 `Runtime:Mode=SelfHosted` 模式；该模式与既有 Azure 模式并存。迁移期间默认行为保持不变，只有明确选择 SelfHosted 才启动该路径；配置缺失时必须显式失败，不得回退到 Azure 或内存持久化。

此验证模式只实现关闭阶段 0 闸门所需的最小垂直切片：

1. 现有 wishlist API 在单个 PostgreSQL 事务中写入业务事件和 outbox。
2. Drasi PostgreSQL CDC source 读取 outbox，运行已验证的连续查询。
3. Drasi HTTP reaction 调用 API 的 Dapr reaction handler；handler 使用 PostgreSQL notification repository 持久化结果。
4. handler 通过现有 broadcaster 发布通知，使通知 SSE/SignalR 客户端收到实际更新。
5. 同一自托管模式下的应用 Agent 使用标准 `IChatClient` 调用用户控制的 OpenAI-compatible 模型端点，验证工具调用及流式响应。

SelfHosted 模式不得解析或启动 Cosmos Change Feed、Key Vault、Event Hubs、Azure 身份或 Azure OpenAI 服务；原 Azure 模式在阶段 0 全部通过前保持可用且不删除。Aspire 管理 API 和 PostgreSQL；Drasi 继续运行于 kind/k3d。模型 endpoint、model id 和 API key 通过 `LLM_BASE_URL`、`LLM_MODEL_NAME`、`LLM_API_KEY` 环境变量配置；密钥不得提交、写入日志或传给非用户控制的服务。

该垂直切片是兼容性验证设施，不代表其他领域仓储已迁移完成。阶段 0 通过后，继续阶段 1–3 迁移其余持久化、事件、打包和部署路径。当前 Drasi 0.10.0 环境未列出 MongoDB source provider；因此本次批准的迁移目标继续使用已验证 PostgreSQL，不引入未经验证的 MongoDB→Drasi 路径。

### 阶段 1：提供者边界

从业务仓储契约中移除 Cosmos 专属 SDK 类型。实现 PostgreSQL 持久化、明确的数据库 schema/迁移以及持久化集成测试。统一 chat-client 构造，并移除其他调用路径中的 Azure OpenAI 客户端直接构造。配置验证必须明确指出数据库、事件或模型设置缺失。

### 阶段 2：持久事件与 Drasi 链路

实现事务 outbox 和已选定的 Drasi 兼容数据源/reaction。运行路径移除 Cosmos Change Feed、Event Hubs、Cosmos 状态存储及 Azure 身份依赖。验证重启恢复、重试、重复投递和错误传递。

### 阶段 3：自托管打包

增加 Aspire 13.6 AppHost、本地启动脚本（创建/复用 kind/k3d、安装 Dapr/Drasi 并启动 AppHost）、Linux Kubernetes 清单、密钥和数据卷配置、就绪检查、备份恢复步骤以及完整本地集成测试配置。更新根目录和服务文档，将自托管部署作为默认路径。

## 6. 验收标准

1. 全新开发环境安装文档列出的容器、.NET/Aspire 13.6、kubectl 和 kind 或 k3d 后，可通过一条文档化命令创建/复用集群、部署 Drasi/Dapr 并启动 Aspire AppHost；不需要 Azure 登录、Azure CLI、azd、Azure 端点变量或 Azure 资源。
2. Linux 主机可使用 Kubernetes 清单部署相同应用代码、配置契约和 Drasi 资源到 K3s 或标准 Kubernetes，不增加 Azure 托管服务。
3. API 写入的数据保存在 PostgreSQL，并在 API/数据库 Pod 重启后仍存在（持久卷按配置工作）。
4. 一条代表性 wishlist 写入会被可靠记录，经 Drasi 处理并反映在相关连续查询结果中，随后通过现有实时 API 契约送达客户端。
5. AI Agent 能使用配置的本地模型完成流式响应及所需工具调用。模型未启动或不可用时，健康检查或请求返回明确错误。
6. 测试覆盖持久化、事件/outbox 行为、Drasi 查询/结果契约、客户端流式更新和本地模型集成。没有测试将进程内存状态误当作持久化存储。
7. 自托管运行时不需要 Azure SDK 凭据、Azure 服务端点、Azure 云资源部署，也不存在隐藏的 Azure 网络调用。历史 Azure 文档可以保留，但必须标为旧路径。
8. 启动、就绪状态、日志及部署脚本能明确指出缺少的依赖或启动失败的组件；必要组件不可用时不得报告整体部署成功。
9. 迁移期间可显式选择 SelfHosted 验证模式；它能完成 API 写入 → PostgreSQL/outbox → Drasi → API/通知存储 → SSE/SignalR 的端到端测试，且不会解析 Azure 凭据或在配置错误时回退到 Azure/内存状态。

## 7. 主要风险与控制措施

| 风险 | 影响 | 控制措施 |
|---|---|---|
| Drasi 版本缺少受维护的自托管数据源或结果适配器 | 阻塞推荐架构或需要自定义集成 | 将兼容性设为阶段 0 闸门；在修改仓储前验证数据源和 reaction |
| Cosmos 类型及 Change Feed 模式渗入持久化和事件发布 | 改造范围大且可能造成行为偏差 | 先建立与提供者无关的契约，再用 API/事件契约测试保护行为 |
| 当前多个事件发布路径可能产生重叠事件 | Drasi 结果重复或顺序变化 | 替换发布器之前明确规范的写入到事件链路及去重要求 |
| 本地模型的性能与工具调用能力随模型和硬件不同 | Agent 功能退化或延迟过高 | 固定经过测试的模型/运行时配置，并在目标硬件上运行工具调用/流式集成测试 |
| 本地 Kubernetes 和模型文件占用较多系统资源 | 开发环境比当前仅启动 API 更复杂 | 发布最低资源建议、就绪检查，以及镜像/模型下载步骤 |
| Aspire 服务容器与 kind/k3d Pod 有独立网络边界 | Drasi 无法访问 Aspire 管理的数据库/事件端点，或 API 无法访问 Drasi | 在阶段 0 验证双向路由、端口暴露和可配置端点；禁止将容器内 `localhost` 当作集群地址 |
| Aspire 13.6 需与 .NET SDK、PostgreSQL hosting integration 和本地模型容器兼容 | AppHost 无法复现完整开发服务图 | 精确固定 Aspire 13.6.0 依赖，建立最小 AppHost 启动/健康检查测试 |
| 现有 Azure 部署脚本和文档让用户以为 Azure 是强制条件 | 用户继续采用旧部署路径 | 将自托管快速入门设为主路径，并将 Azure 内容标记为可选旧路径 |

## 8. 评估结论

项目可以改造成不依赖 Azure 的自托管应用，但这属于高复杂度现代化改造，不是替换部署模板即可完成。最重要的工作是替换 Cosmos 专属持久化和 Change Feed 链路，同时保留 Drasi 行为。推荐开发机使用 Aspire 13.6 编排 API、数据库和本地模型，并由启动脚本负责创建 kind/k3d 与部署 Drasi；Linux 主机使用 Kubernetes 清单运行完整自托管服务。应先验证 Drasi 兼容性及 Aspire/集群网络，再重构存储和 AI 提供者。本地模拟器可以帮助缩短部分开发反馈周期，但不满足长期自托管服务器目标。
