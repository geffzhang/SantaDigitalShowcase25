# 脱离 Azure 自托管改造：计划索引

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按已批准的规格将项目改造成不依赖 Azure 的本地开发及 Linux 自托管应用。

**Architecture:** 把改造拆成四个可分别测试的工作流：持久化/Drasi、AI 提供者、Aspire/kind 开发启动、Linux Kubernetes 部署。Drasi 兼容性是第一道闸门；本地 Aspire 与 Linux Kubernetes 部署复用应用配置契约和 Drasi 资源定义，但采用各自合适的编排方式。

**Tech Stack:** .NET 9、PostgreSQL、Drasi、Dapr、.NET Aspire 13.6.0、kind 或 k3d、K3s/标准 Kubernetes、本地 OpenAI-compatible 模型服务。

## Global Constraints

- 不需要 Azure 登录、Azure 托管服务、Azure 凭据或 Azure 端点即可构建、启动、测试及运行目标环境。
- 保留 API/事件语义、Drasi 查询、Agent 工具调用和 SSE/SignalR 客户端契约。
- Drasi 非 Azure 数据源及 reaction 必须先验证；验证失败时停止相关迁移并取得用户对替代路线的确认。
- 开发机使用 .NET Aspire 13.6.0；启动脚本创建或复用 kind/k3d 集群，默认不得删除集群或持久数据。
- API 保持 net9.0；本地模型必须验证流式响应及 Agent 工具调用。
- Linux 部署面向 K3s/标准 Kubernetes，密钥和持久数据不得写入仓库。

---

## 工作流与依赖

1. **数据持久化与 Drasi**（[2026-10-04-selfhosted-data-drasi.md](./2026-10-04-selfhosted-data-drasi.md)）：先完成 Drasi source/reaction 的候选验证；完整发布闸门还必须通过本地 AI 与 Aspire/kind 网络验证。未通过完整闸门不得移除现有 Cosmos/Event Hubs 运行路径。
2. **本地 AI 提供者**（[2026-10-04-local-ai-provider.md](./2026-10-04-local-ai-provider.md)）：可与 PostgreSQL schema 的增量准备并行；其工具调用和流式集成测试必须在完整兼容性闸门关闭前通过。与数据计划均会修改 `src/Program.cs`，必须顺序合并该文件的依赖注入改动。
3. **Aspire 13.6 与 kind/k3d 开发启动**（[2026-10-04-aspire-kind-development.md](./2026-10-04-aspire-kind-development.md)）：在应用配置键确定后执行；完成双向网络测试，并在 Aspire 托管服务就绪后验证 Drasi source/reaction，作为完整发布闸门的一部分。
4. **Linux 自托管 Kubernetes 部署**（[2026-10-04-linux-self-hosted-deployment.md](./2026-10-04-linux-self-hosted-deployment.md)）：依赖数据、AI、Drasi 及配置契约稳定。

数据计划 Task 2 可在兼容性验证期间增量准备；Task 3/4 在 Task 1 的 Drasi 候选验证、本地 AI 工具调用验证、Aspire/kind 双向网络和代表性查询/实时输出全部通过前不得移除 Azure 运行路径。若任一发布闸门失败，按规格停止并取得用户对替代路线的决定。

每份子计划都应单独执行并验证。所有代码改动使用测试先行；遇到标记为闸门的条件失败时停止，不得用内存后备、云服务或伪造成功绕过。
