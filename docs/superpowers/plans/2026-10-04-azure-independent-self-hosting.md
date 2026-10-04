# 脱离 Azure 自托管改造：计划索引

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按已批准的规格将项目改造成不依赖 Azure 的本地开发及 Linux 自托管应用。

**Architecture:** 先执行阶段 0A，增加与 Azure 路径并存的可选 SelfHosted 验证运行模式，以关闭需要真实 API 的兼容性闸门；随后按数据/Drasi、AI 提供者、Aspire/kind 开发启动、Linux Kubernetes 部署四个工作流完成全量迁移。Drasi 兼容性闸门通过前不得删除 Azure 运行路径。

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

0. **SelfHosted 阶段 0A 验证模式**（[2026-10-04-selfhosted-validation-mode.md](./2026-10-04-selfhosted-validation-mode.md)）：新增 opt-in runtime、最小 PostgreSQL wishlist/outbox/notification 路径、项目 Agent 的 self-hosted model wiring、Aspire API/PostgreSQL 和真实 Drasi→API→SSE/SignalR 检查。Azure 模式保持默认且可用。
1. **数据持久化与 Drasi**（[2026-10-04-selfhosted-data-drasi.md](./2026-10-04-selfhosted-data-drasi.md)）：阶段 0A 关闭真实 API 闸门后，扩展 PostgreSQL/outbox 实现并迁移剩余仓储。Drasi 0.10 当前内置 PostgreSQL source 已验证；MongoDB provider 不在已安装 source provider 列表中。
2. **本地 AI 提供者**（[2026-10-04-local-ai-provider.md](./2026-10-04-local-ai-provider.md)）：阶段 0A 先接通自托管 `IChatClient`，并行保留 Azure AI 注册。工具调用和流式集成测试通过前不得删除 Azure OpenAI 包/路径。
3. **Aspire 13.6 与 kind/k3d 开发启动**（[2026-10-04-aspire-kind-development.md](./2026-10-04-aspire-kind-development.md)）：阶段 0A 先验证 API/PostgreSQL 与 Drasi 的真实双向网络；随后补齐幂等启动/停止脚本和开发文档。
4. **Linux 自托管 Kubernetes 部署**（[2026-10-04-linux-self-hosted-deployment.md](./2026-10-04-linux-self-hosted-deployment.md)）：依赖数据、AI、Drasi 及配置契约稳定。

Drasi PostgreSQL CDC/query/HTTP reaction 候选链路和直接模型服务 tool-call/SSE 能力已在隔离环境验证，但尚未通过项目 API、Aspire/kind 网络和客户端实时输出的完整闸门。阶段 0A 先增加并行运行模式，不删除或替换 Azure 路径；只有真实 API 写入、事务 outbox、Drasi query/reaction、通知持久化、Agent 工具调用及 SSE/SignalR 网络验证全部通过后，才执行后续移除旧 Azure runtime 的任务。任何候选或网络闸门失败均保持 **Blocked** 并记录证据。

每份子计划都应单独执行并验证。所有代码改动使用测试先行；遇到标记为闸门的条件失败时停止，不得用内存后备、云服务或伪造成功绕过。
