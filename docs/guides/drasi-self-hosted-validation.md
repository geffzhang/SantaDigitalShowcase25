# Drasi 自托管兼容性验证

**状态：候选 source/reaction smoke test 通过；完整发布闸门未通过。**

## 已验证环境

以下验证在隔离的 kind 集群 `santa-local` 中进行，使用独立 kubeconfig；未修改原有 Kubernetes context。

| 组件 | 版本或镜像 |
| --- | --- |
| kind | v0.33.0 |
| Kubernetes 节点 | v1.37.0 |
| Drasi CLI 与控制平面 | v0.10.0 |
| Drasi source/query/reaction 镜像 | `ghcr.io/drasi-project/*:0.10.0` |
| Dapr | v1.14.5 |
| PostgreSQL smoke-test source | `postgres:16-alpine` |

## 已通过的候选验证

1. 在 PostgreSQL 16 中启用 logical WAL，并创建含主键的 `wishlist_events` 表。
2. Drasi PostgreSQL CDC source `wishlist-smoke` 成为 available。
3. 连续查询 `wishlist-updates-smoke` 进入 Running，返回 `id`、`childId`、`text`、`type`、`dedupeKey` 和 `createdAt` 字段。
4. 插入唯一 smoke-test 记录后，`drasi watch wishlist-updates-smoke` 返回对应数据。
5. Drasi HTTP reaction `wishlist-smoke-http` 成为 available，并订阅该 query。插入新记录后，reaction 将模板渲染为 JSON，并向集群内 HTTP receiver 发出 POST，接收端返回 HTTP 200。例如验证到的请求体为：

   ```json
   {"id":"smoke-event-6","childId":"child-6","text":"Payload validation card","type":"wishlist","dedupeKey":"dedupe-smoke-6"}
   ```

## 尚未验证及发布阻塞项

- HTTP reaction 目前投递到临时 smoke-test receiver；尚未验证真实应用 API 的接收、SSE 或 SignalR 客户端事件链。
- 尚未完成 Aspire 与 kind Pod 之间的双向网络连通测试。
- 用户控制的 OpenAI-compatible 自托管远端模型已通过通用 tool-call 探测（模型调用函数并返回预期的 `42`）及 HTTP 200 SSE 流式探测（收到内容增量和正常 `finish_reason`）。尚未验证项目内 Agent Framework 的接线或真实 API 流。
- 此测试使用独立的 PostgreSQL smoke-test 表，不代表应用仓储、事务 outbox 或生产 Drasi manifests 已迁移完成。

因此，本结果只证明 Drasi 0.10.0 的 PostgreSQL CDC 候选 source、查询和 HTTP reaction 能在该 kind 环境中工作；不得据此移除 Cosmos DB/Event Hubs 路径或宣称完整自托管发布闸门通过。后续步骤和停止条件见[自托管数据与 Drasi 实施计划](../superpowers/plans/2026-10-04-selfhosted-data-drasi.md)及[Aspire/kind 开发计划](../superpowers/plans/2026-10-04-aspire-kind-development.md)。

## MongoDB 候选检查

2026-10-04 在同一 Drasi 0.10.0 kind 环境运行 `drasi list sourceprovider -n drasi-system`，实际可用 source provider 为 `PostgreSQL`、`MySQL`、`SQLServer`、`CosmosGremlin`、`Dataverse`、`EventHub` 和 `Kubernetes`；其中没有 MongoDB provider。因此，MongoDB Change Streams 不能直接配置为当前已安装 Drasi 的原生 source。此检查不排除开发或安装自定义 Drasi source adapter，但在该适配器经过验证前，MongoDB → Drasi 的候选链路为 **Blocked**。
