# .NET 10 升级设计

**状态：** 已获用户批准的设计

**日期：** 2026-10-04

## 目标

将仓库中的 .NET 构建、测试和发布入口统一升级到 .NET 10，同时维持现有 Azure 与 SelfHosted 两条运行路径及应用行为。

## 范围

- 将 `src/src.csproj`、`tests/Tests.csproj` 和 `AppHost/AppHost.csproj` 的目标框架升级到 `net10.0`。
- 将 `Dockerfile.multi` 与 `src/Dockerfile` 中的 .NET SDK 和 ASP.NET runtime 镜像改为 .NET 10。
- 对齐必须与目标框架共同升级的依赖：
  - EF Core、Npgsql EF Core provider 与 EF Core Design 使用兼容的稳定版 10.x 组合。
  - ASP.NET Core 测试宿主与 MVC 测试包使用兼容的稳定版 10.0.x。
  - Aspire AppHost SDK 与所有 `Aspire.Hosting.*` 包维持同一版本；只有在验证现有 13.6.0 不兼容时才升级到兼容的稳定版。
  - 其他依赖只在 restore、构建或测试证明存在兼容性阻塞时调整，不进行整体依赖刷新。
- 更新 README 和当前使用的运行/验证指南中的 .NET 版本及相关命令。历史设计文档和已完成计划保留原样，作为当时技术决策的记录。
- 不新增 `global.json`，除非后续发现仓库已有 SDK 锁定或部署环境需要明确的 SDK 选择策略。

## 保持不变

- 保留 Azure 部署定义、Azure 相关集成和 SelfHosted Aspire 运行方式；本次不移除或迁移任一运行路径。
- 不改变 API、数据模型、模型服务端点配置、密钥注入方式、Drasi/PostgreSQL 拓扑或用户可见行为。
- 不做 net9.0/net10.0 多目标，也不捆绑无关的 NuGet 更新或重构。
- 不要求执行 Azure 云端部署；验证通过仓库构建、测试和容器构建完成。

## 依赖与兼容性约束

- 先确认稳定版包对 `net10.0` 的支持，再确定精确补丁版本；不使用预览包。
- EF Core 及其 provider 必须是兼容组合，避免混用 9.x 与 10.x 主版本。
- Aspire AppHost SDK 与 Hosting 包保持版本一致。
- Docker SDK/runtime 镜像使用相同 .NET 主版本 10；容器入口、端口和前端构建步骤不变。
- 如果发现必须进行超出上述范围的破坏性 API 或业务调整，应暂停并重新确认设计，不通过静默降级或掩盖错误来完成迁移。

## 验证标准

1. 对完整 solution 执行 restore、Release build 和测试；确认无失败，且构建输出无新的框架兼容性错误。
2. 分别构建 `Dockerfile.multi` 和 `src/Dockerfile`，验证两种发布入口都能生成 .NET 10 镜像。
3. 构建 AppHost，并按可用环境运行 SelfHosted Aspire/Drasi smoke，确认 API、PostgreSQL、Drasi 查询和通知链路可用。
4. 运行与 SelfHosted 验证有关的脚本契约测试；Azure 路径至少确认其项目引用、容器发布入口和文档未被本次迁移破坏。
5. 检查改动中没有 API key、模型密钥或其他凭据。

## 主要风险与处理

| 风险 | 处理 |
|---|---|
| EF Core 10 与现有 Npgsql provider 不兼容 | 选择兼容的 provider 主版本，并运行 PostgreSQL 集成测试及 migration 测试。 |
| Aspire 包与 .NET 10 TFM 不兼容 | 保持 Aspire 包成组升级；只有 restore/build 验证需要时才提高 Aspire 版本。 |
| Docker 发布入口遗漏旧镜像标签 | 对两个 Dockerfile 分别构建，避免只验证本地 SDK 构建。 |
| Azure SDK 或 AI 依赖与新运行时存在行为差异 | 不做全面升级；以现有测试及构建验证，若暴露兼容性阻塞再提出最小修复。 |
