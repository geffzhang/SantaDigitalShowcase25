# Aspire 编排 Drasi Server 设计

**日期：** 2026-10-04  
**状态：** 设计内容已逐段获用户批准，等待规格文件审阅

## 1. 目标与范围

为 SelfHosted 模式建立单一 Aspire 启动路径，在本机编排前端、API、PostgreSQL 和独立 Drasi Server，不依赖 Azure 云资源、kind 或 Kubernetes。用户配置的 OpenAI-compatible 模型 endpoint 仍是外部服务；模型 endpoint、模型名和 API key 继续通过环境配置与 Aspire secret 参数传入。

保留现有 Azure runtime、Azure 服务注册和部署路径。SelfHosted 的 wishlist/outbox、Drasi 查询、通知持久化以及 SSE/SignalR 行为继续可用。Drasi Server 取代 SelfHosted 对 Drasi Platform/Kubernetes、Dapr reaction 和 Drasi view Service 的依赖。

本设计取代 [kind 私有 Docker 网络设计](./2026-10-04-kind-private-network-design.md) 作为 SelfHosted 目标架构；该已提交规格保留为历史决策记录。尚未提交的 kind 网络实施计划及 kind 专用实现不属于新目标，后续计划应明确淘汰它们。

## 2. 依据与选型

用户指定采用 [Drasi Server](https://github.com/drasi-project/drasi-server)。其官方仓库将它描述为独立运行的实时数据变更处理服务，并提供 Docker 启动方式、PostgreSQL CDC source、Cypher continuous query、HTTP/SSE reaction 和 REST API。当前发布的容器镜像为 `ghcr.io/drasi-project/drasi-server:0.2.3`；实现固定版本，不使用浮动的 `latest`。

Drasi Server 与 Drasi Platform 的 REST API 并非兼容部署模式：Server 使用 `/api/v1/*`、`/queries` 和 `{success, data, error}` 响应包装；Platform 使用 `/v1/*`、`/continuousQueries` 并返回原始对象。差异见上游 [drasi-platform#426](https://github.com/drasi-project/drasi-platform/issues/426)。因此 SelfHosted 必须采用 Drasi Server 专用 API adapter，不能只更换 `DRASI_VIEW_SERVICE_BASE_URL`。

## 3. 目标资源拓扑

Aspire AppHost 统一管理以下本地资源：

| 资源 | 运行方式 | 连接与暴露边界 |
|---|---|---|
| 前端 | 现有 Aspire Vite app | 通过本机 loopback 访问 API |
| SelfHosted API | 由现有 `src/Dockerfile` 构建的容器 | 容器网络内部监听 HTTP；宿主机映射仅绑定 loopback |
| PostgreSQL | Aspire PostgreSQL 容器与持久化数据卷 | 容器网络内使用 PostgreSQL 服务引用；宿主机映射仅绑定 loopback |
| Drasi Server | 固定版本 `ghcr.io/drasi-project/drasi-server:0.2.3` 容器 | 通过 Aspire 服务引用访问 API/PG；管理 UI/API 的宿主机映射仅绑定 loopback |
| 模型服务 | 用户控制的外部 OpenAI-compatible endpoint | API 出站访问；API key 只经 Aspire secret 参数注入 |

API、PostgreSQL 与 Drasi Server 通过 Aspire 管理的容器网络及服务引用通信，不配置静态容器 IP、kind NodePort、Kubernetes Service/EndpointSlice 或宿主机 wildcard 绑定。Drasi Server 的配置文件提供 PostgreSQL source、continuous queries 和 HTTP reactions；敏感值通过环境变量占位符注入，不写入 YAML。

PostgreSQL 必须启用 logical WAL、足够的 replication slots 与 WAL senders，并为 Drasi source 提供具备所需 replication 权限的数据库身份。Drasi Server 的配置和索引持久化行为需明确设置；默认方案不依赖 Drasi 本地索引代替 PostgreSQL 中的权威数据。

空数据卷首次启动时，Aspire 必须先确认 API 的 EF migrations 完成，再启动 Drasi Server 的 CDC source；API 的 migration readiness 不依赖 Drasi readiness。

## 4. 数据流与应用边界

1. SelfHosted API 在同一 PostgreSQL 事务中写入 wishlist 记录和 append-only outbox event。
2. Drasi Server PostgreSQL source 通过 CDC 读取 outbox 表；source 的表名、主键和 bootstrap 行为显式配置。
3. Drasi Server 运行项目 SelfHosted 使用的 wishlist 更新、趋势、重复项、非活跃用户及行为/推荐查询。当前查询定义见 [Drasi resource 文件](../../../drasi/resources/drasi-resources.yaml)；旧 Kubernetes/Drasi Platform 查询语法需迁移到 Drasi Server 配置，并逐项验证结果字段及时间语义。
4. 对应 HTTP reaction 使用明确的 JSON body template 调用 SelfHosted API。API 验证事件数据，先持久化 notification，持久化成功后才返回成功确认并发布 SSE/SignalR 事件。
5. API 中需要读取 Drasi query 结果的服务，经 Drasi Server REST API adapter 请求相应 query 并处理响应 envelope。

SelfHosted 使用 Drasi Server adapter；Azure runtime 保持既有 Drasi Platform/Dapr 客户端与反应路径。运行模式选择不得使 Azure 注册意外依赖 Drasi Server，也不得使 SelfHosted 回退到 Kubernetes DNS、旧的 Drasi Platform endpoint 或空结果成功响应。

## 5. 启动、配置与失败行为

- Aspire 先等待 PostgreSQL ready，再启动 API。SelfHosted API 的 migration-ready 信号只在 EF migrations 成功后可用，且不依赖 Drasi；Drasi Server 等待该信号后再启动 CDC source，避免先于表结构启动或形成循环依赖。
- Drasi Server 的 PostgreSQL source 在 source/CDC 不可用时必须呈现失败状态；Aspire readiness 与验证脚本不得仅因容器进程存活就判定整条链路通过。
- API、Drasi Server、PostgreSQL 的宿主机端口仅供本机使用。容器间流量走 Aspire 内部服务地址。
- `LLM_API_KEY`、数据库口令及任何 webhook credential 使用 Aspire secret/environment 参数传递；不得提交到配置、日志、manifest 或测试报告。Drasi 配置文件只引用变量名。
- Drasi REST API 或 CDC 不可用时，相关 API 查询或集成验收必须明确失败；禁止静默忽略异常、回退到旧 K8s 地址或返回 success-shaped 空结果。
- Drasi Server 的 standalone 与 Platform REST API 差异由不同 adapter 隔离；错误响应、超时及无法解析的 JSON 必须作为失败处理。

## 6. 验收标准

### 6.1 构建与启动

- AppHost 能构建并启动前端、SelfHosted API 容器、PostgreSQL 和 Drasi Server `0.2.3`，无需 `kind`、`kubectl` 或 Drasi Platform CLI。
- 在空数据卷启动时，API migrations 必须先成功，随后 Drasi Server source/query 才能 ready；PostgreSQL logical replication 设置和各 readiness 状态均可明确验证。
- Docker 端口映射证明所有宿主机发布端口只绑定 loopback；本地 UI/API/PostgreSQL 维护路径保持可用。
- Azure 默认 runtime 与现有 Azure 路径的相关测试保持通过。

### 6.2 PostgreSQL CDC 到通知

- 经 SelfHosted API 写入唯一 wishlist/outbox 记录后，Drasi Server PostgreSQL source 收到对应变更，目标 query 返回该记录。
- Drasi Server 的 HTTP reaction 回调 SelfHosted API；测试证明 notification 持久化先于成功确认，随后 SSE 和 SignalR 收到预期事件。
- 持久化失败时 reaction handler 返回错误且不发布实时事件；无效 payload 返回 4xx。
- API 经 Drasi Server REST API adapter 查询目标 query 时，能正确解析 `{success, data, error}` envelope，并拒绝错误 envelope/状态码。
- 上述完整集成闸门通过前，SelfHosted Stage 0 仍保持未通过；构建或单元测试不能代替 CDC→reaction→notification→SSE/SignalR 实测。

## 7. 实施停止条件与风险

Drasi Server 为独立产品且当前固定版本为 `0.2.3`。实施期间必须在实际容器中验证 PostgreSQL CDC 权限、查询语法/函数、bootstrap 语义、HTTP reaction payload 以及 REST 响应形状。若项目查询无法由该版本正确表达，或其容器无法通过 Aspire 可靠启动，停止该路径并报告阻塞项；不得静默删减查询、恢复 Kubernetes 依赖或放宽宿主机监听边界。

---

**已审阅的上游资料**

- [Drasi Server 官方仓库与 README](https://github.com/drasi-project/drasi-server)
- [Drasi Server 官方 0.2.3 发布页](https://github.com/drasi-project/drasi-server/releases/tag/0.2.3)
- [Drasi Server PostgreSQL CDC 配置示例](https://github.com/drasi-project/drasi-server/blob/main/examples/configs/02-sources/postgres-cdc-complete.yaml)
- [Drasi Server HTTP webhook reaction 配置示例](https://github.com/drasi-project/drasi-server/blob/main/examples/configs/03-reactions/http-webhook-sender.yaml)
