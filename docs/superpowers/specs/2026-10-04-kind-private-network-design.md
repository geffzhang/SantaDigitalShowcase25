# kind 私有 Docker 网络设计

**日期：** 2026-10-04

**状态：** 设计已由用户逐段批准；实施尚未开始

## 1. 目标与范围

为 SelfHosted 本地验证模式建立一条不依赖 Windows 宿主机向外网卡暴露服务的 kind 网络路径，使 Drasi Pod 能访问 Aspire 管理的 API 和 PostgreSQL，并使 API 能访问 Drasi view service。

本设计保留 Azure runtime 和既有 SelfHosted 数据路径，不改变业务 API 或模型提供者行为。模型 endpoint、模型名和 API key 仍按现有 AppHost 配置传入；API key 使用 Aspire secret 参数，不写入规格、Drasi manifest 或日志。

## 2. 当前环境与约束

- AppHost 使用 Aspire 13.6；当前 API 是 Windows 上的 `AddProject` 进程，PostgreSQL 是 Aspire 容器。
- AppHost API 与 PostgreSQL 当前分别只通过宿主机 loopback 端口 `8081`、`5433` 访问。
- 现有 kind Docker bridge 名为 `kind`，观测到的网段为 `172.19.0.0/16`；当前 control-plane 容器地址为 `172.19.0.2`。
- kind Kubernetes Pod 使用独立 Pod CIDR；本次观测为 `10.244.0.0/24`。Drasi view service 当前是 `ClusterIP`。
- Windows 宿主机没有 `172.19.0.0/16` 地址。因此 Windows 进程不能直接加入 Docker bridge；Docker 网络容器名/alias 也不会自动成为 Kubernetes Pod 的 DNS 记录。
- [src/Dockerfile](../../../src/Dockerfile) 可作为 API 容器构建入口。Aspire `AddDockerfile` 与 `WithContainerRuntimeArgs` 是可评估的实现接口，但外部 Docker network 接入尚未经过本项目验证。

以上 IP、端口和集群名均是观测值，不得作为静态地址写入应用配置。

## 3. 目标架构

### 3.1 Aspire 管理 API 与 PostgreSQL

- PostgreSQL 继续由 Aspire PostgreSQL resource 管理，并保留开发数据卷。
- API 改由现有 [src/Dockerfile](../../../src/Dockerfile) 构建为容器；容器内监听 `0.0.0.0:80`，宿主机发布端口仍仅绑定 `127.0.0.1:8081`。
- PostgreSQL 宿主机映射仍仅绑定 `127.0.0.1:5433`，容器内服务端口为 `5432`。
- API 与 PostgreSQL 都必须在容器启动时接入已经存在的 Docker `kind` 网络，并在该网络上具有稳定且不冲突的容器 alias。API 使用容器网络内的 PostgreSQL 地址，不通过宿主机 loopback 访问数据库。
- API 继续设置 `Runtime__Mode=SelfHosted`、AI endpoint/model 和 secret API key，并等待 PostgreSQL ready。现有启动迁移服务仍负责在 API 接收请求前应用 EF migrations。
- 不能假定 Aspire 有“加入指定外部 Docker network”的一等配置。若以 `WithContainerRuntimeArgs` 传入 Docker runtime 参数，必须验证 DCP 的最终网络、alias、端口映射、health/readiness 和生命周期行为；若发生网络参数冲突或资源未实际加入 `kind`，本方案不得宣称可用。

### 3.2 Kubernetes Service 与 EndpointSlice

在 Drasi 所在 namespace 中创建无 selector 的 ClusterIP Service，并由本地同步脚本维护 EndpointSlice：

| Service | EndpointSlice 目标 | 端口 | 用途 |
|---|---|---:|---|
| `selfhosted-api` | 当前 API 容器的 Docker bridge IPv4 | 80 | Drasi HTTP reaction 调用 API |
| `selfhosted-postgres` | 当前 PostgreSQL 容器的 Docker bridge IPv4 | 5432 | Drasi PostgreSQL source 读取事件 |

脚本必须从 Docker 当前网络状态动态读取地址，核验资源 namespace/context 和所有权标签，只更新这两个明确命名的 Service/EndpointSlice。应用容器重建后需重新同步，不保留失效 IP。Drasi 配置使用 Kubernetes Service DNS，而不是 Docker alias 或裸容器地址。

### 3.3 API 到 Drasi 的反向路由

- 保持既有 Drasi view ClusterIP Service 不变。
- 新增独立的 kind-only NodePort Service，选择现有 Drasi view service 的后端 Pod。使用独立 Service 可避免直接修改由 Drasi 管理的 ClusterIP Service。
- NodePort 由当前集群分配并动态读取；API 的 `DRASI_VIEW_SERVICE_BASE_URL` 指向 kind control-plane 在 Docker `kind` 网络上的动态地址和该 NodePort。
- NodePort 不配置宿主机端口映射；预期入口仅为 kind node 的 Docker `kind` 网络接口，不是公网入口。由于 Docker Desktop/WSL2 路由可能因环境而异，验收仍须检查 Windows 宿主机及非预期网络接口的实际可达性。
- API 查询必须通过该 URL 实际读取 Drasi view；只验证 URL 格式或 TCP 端口不足以通过验收。

## 4. 流量路径

1. 本机前端通过 `http://localhost:8081` 访问 API；需要本机数据库维护时通过 `localhost:5433` 访问 PostgreSQL。
2. Drasi PostgreSQL source 通过 `selfhosted-postgres.<namespace>.svc.cluster.local:5432` 读取 PostgreSQL。
3. Drasi HTTP reaction 通过 `selfhosted-api.<namespace>.svc.cluster.local:80` POST CloudEvent 到现有 API handler。
4. API 容器通过 kind control-plane Docker 地址和动态 NodePort 查询 Drasi view service。
5. 任意路径都不得将宿主机 API 或 PostgreSQL 监听改为 `0.0.0.0`，也不得用 `localhost` 作为 Pod 可访问的地址。

Kubernetes Service/EndpointSlice 提供服务发现，不提供应用层身份认证。此设计的信任边界仅是开发者本机上的 kind 集群和 Docker `kind` 网络；不将该配置用于共享或不可信集群。

## 5. 启动、同步与失败策略

1. 启动前确认 Docker daemon、预期 Kubernetes context、kind control-plane、Docker `kind` 网络和必要端口；创建或确认独立 Drasi view NodePort Service，读取当前 node 地址和已分配 NodePort，并在 API 启动前设置 `DRASI_VIEW_SERVICE_BASE_URL`。
2. Aspire 启动带数据卷的 PostgreSQL 和 SelfHosted API。API 必须通过容器网络 alias 连接数据库，并在 migrations 成功后 ready。
3. 同步脚本通过 Docker 检查实际容器网络地址，更新 API/PG EndpointSlice；重新读取并核验其 IPv4、端口和 Service namespace。
4. 运行双向网络闸门；任一步失败均以非零状态退出，记录失败方向和目标类别，不打印 secret。
5. 容器重启、Pod IP/NodePort 改变或 context 变化后必须重新发现并同步；若 kind node 地址或 NodePort 改变，更新 API 配置并重启 API 后再验证。不得静默使用旧地址或成功形状的 fallback。
6. 若 Pod 无法路由至 Docker bridge 容器，或 DCP 无法可靠接入外部 network，保持 Stage 0 网络状态为 **Blocked**，停止本路径实施并重新评估将 API/PG 运行于 Kubernetes workloads 的方案。不得通过 wildcard host binding 绕过失败。

## 6. 验收标准

### 6.1 容器与绑定

- AppHost build 和启动成功；PostgreSQL 数据卷保留数据；API 使用 SelfHosted runtime 并完成启动迁移。
- Docker inspect 证明 API 与 PostgreSQL 都连接到当前 `kind` 网络，且预期 aliases 可从该网络解析。
- API 启动迁移通过容器网络 alias 和 PostgreSQL `5432` 成功连接数据库，不依赖宿主机 loopback 映射。
- API 健康检查和 PostgreSQL-backed audit endpoint 经本机 loopback 返回成功。
- PostgreSQL 对 Windows 宿主机的映射仅绑定 loopback；API 的本机映射亦仅绑定 loopback。

### 6.2 集群双向网络

- 从实际 Drasi probe Pod 经 Kubernetes Service DNS 请求 API `/healthz` 成功。
- 从实际 Drasi probe Pod 对 PostgreSQL Service 的 `5432` TCP 探测成功；Drasi source 能读取 PostgreSQL 中的目标表。
- EndpointSlice 当前地址与 Docker inspect 的容器 bridge IP 一致，端口分别为 API `80`、PG `5432`。
- API 应用路径经 NodePort 读取到 Drasi view 的已知查询结果；不得只用 Windows 宿主机发出的 HTTP 请求代替 API→Drasi 验证。
- NodePort 没有宿主机发布映射；Drasi view ClusterIP Service 未被替换或删除；实际可达范围符合 kind-only 信任边界。

### 6.3 端到端业务链路

网络闸门通过不等于 SelfHosted 阶段完成。还须验证唯一 wishlist item 从 API 写入 PostgreSQL/outbox，经 Drasi query 和 HTTP reaction 回到 API，产生持久化 notification，并由 SSE 与 SignalR 收到预期事件。任一数据、反应或实时推送步骤失败，完整 Stage 0 仍为 **Blocked**。

## 7. 实施边界与风险

- 首先验证 Aspire DCP 对外部 `kind` Docker network 的实际支持，以及 kind Pod 到 bridge 容器 IP 的路由。不能凭 `WithContainerRuntimeArgs` API 存在就推断接入成功。
- EndpointSlice 目标是 Kubernetes Pod 网络之外的 Docker bridge 地址；kind CNI、Windows/WSL2 Docker 网络和节点路由的实际行为需以 probe Pod 测试为准。
- Docker 容器 IP 会改变，因此 EndpointSlice 同步必须与容器生命周期配合，并且地址过期时要快速失败。
- Drasi view NodePort 只面向 kind Docker 网络接口配置，且不发布宿主机端口；若实测可从该信任范围之外访问，网络闸门不得通过。若未来 cluster 或 Docker 网络共享范围扩大，应重新评估认证与访问控制。
- 不在本任务中移除 Azure DI、迁移 Azure 部署、改变模型调用逻辑、开放任意网卡端口或重建现有 kind 集群。

## 8. 关联工作

本设计细化 [SelfHosted validation mode plan](../plans/2026-10-04-selfhosted-validation-mode.md) 中 Task 6 的 kind 网络步骤及 Task 7 的 Drasi HTTP reaction 验收。只有本规格的网络验收和该计划的完整业务链路验收均通过，才能将对应阶段标记为 Passed。
