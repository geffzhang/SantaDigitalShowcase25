# 本地 AI 提供者实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `Runtime:Mode=SelfHosted` 下让所有 Agent 使用可配置的用户控制 OpenAI-compatible 模型端点；阶段 0 完整闸门通过前保留 Azure AI 模式。

**Architecture:** 把 `IChatClient` 的建立集中到配置验证过的 factory/DI registration。Azure 模式继续使用现有 Azure OpenAI client；SelfHosted 模式使用标准 OpenAI-compatible endpoint。主 Agent、AG-UI Agent、RecommendationService 和 MultiAgentOrchestrator 均从同一模式选择的注册获取客户端；Agent system prompt、工具定义、SSE/API 契约维持现状。

**Tech Stack:** .NET 9、Microsoft Agent Framework、Microsoft.Extensions.AI、Microsoft.Extensions.AI.OpenAI 现有 package、用户控制的本地或远端 OpenAI-compatible inference server。

## Global Constraints

- SelfHosted 模型运行在开发机或用户控制的自有主机，不依赖 Azure endpoint、Entra token 或第三方云模型 API。
- API 内配置键为 `AI:Endpoint`、`AI:Model`、`AI:ApiKey`；Aspire/开发 shell 从 `LLM_BASE_URL`、`LLM_MODEL_NAME`、`LLM_API_KEY` 映射。API key 不得写入仓库、日志、dashboard 或测试报告。
- 保留现有 Agent 名称、system prompts、tool schemas、SSE 事件格式及 tool-calling 行为。
- 模型不可用时明确报错；任何保留的 deterministic fallback 必须在 API/结果中明确标记，不能伪装成模型成功。
- `src/src.csproj` 维持 `net9.0`。
- 阶段 0 完整兼容性闸门通过前，保留 Azure OpenAI DI 分支和 NuGet package；只允许重构成由 `Runtime:Mode` 选择的双 provider 实现。

---

## 文件结构与边界

- 创建 `src/lib/AiOptions.cs`：仅定义/验证模型配置。
- 创建 `src/lib/ChatClientRegistration.cs`：仅创建并注册标准 OpenAI-compatible `IChatClient`。
- 修改 `src/Program.cs`、`src/services/AgUiEndpoints.cs`、`src/services/MultiAgentOrchestrator.cs`、`src/services/RecommendationService.cs`：统一注入客户端。
- 修改 `src/appsettings.json`：添加无凭据的默认端点/模型配置示例。
- 修改 `tests/unit/RecommendationServiceTests.cs` 并创建 `tests/unit/ChatClientRegistrationTests.cs`。

## Task 1：统一 AI 配置及客户端工厂

**Files:**
- 创建：`src/lib/AiOptions.cs`
- 创建：`src/lib/ChatClientRegistration.cs`
- 修改：`src/appsettings.json`
- 修改：`src/src.csproj`（仅当现有 `Microsoft.Extensions.AI.OpenAI` 无法提供标准 endpoint 构造 API 时）
- 创建：`tests/unit/ChatClientRegistrationTests.cs`

**Interfaces:**

```csharp
public sealed class AiOptions
{
    public const string SectionName = "AI";
    public string Endpoint { get; init; } = "";
    public string Model { get; init; } = "";
    public string ApiKey { get; init; } = "";
}
```

Factory 提供按 `Runtime:Mode` 选择的 `AddRuntimeChatClient(IServiceCollection, IConfiguration)` 扩展方法，注册 singleton `IChatClient`。SelfHosted 分支使用标准 `OpenAI.Chat.ChatClient`/`OpenAIClientOptions.Endpoint`；Azure 分支保留 `Azure.AI.OpenAI.AzureOpenAIClient` 构造并返回同一 `IChatClient` abstraction。

- [ ] **步骤 1：先写选项验证测试**

在 `ChatClientRegistrationTests` 验证缺少 Endpoint/Model 时 options validation 报出包含缺失键名的错误；SelfHosted 有效 endpoint/model 能创建 DI service descriptor；Azure 模式仍能使用现有 Azure 配置。SelfHosted 的 API key 可留空或由用户控制服务配置，但不得读取/记录其他 provider 的 credential。

- [ ] **步骤 2：运行目标测试确认未实现**

运行 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~ChatClientRegistrationTests`。预期配置选项/factory 类型未定义而失败。

- [ ] **步骤 3：实现 options、factory 和配置**

将 options 注册为启动时验证；SelfHosted factory 以 `OpenAIClientOptions.Endpoint = new Uri(options.Endpoint)` 构造标准 OpenAI client，再调用 chat client 的 `.AsIChatClient()`。Azure factory 继续使用现有 endpoint、deployment 和 API key/managed identity 配置。`appsettings.json` 仅放无凭据默认值；真实配置从环境变量或 user secrets 提供。

- [ ] **步骤 4：运行目标测试**

重跑 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~ChatClientRegistrationTests`，预期有效配置能解析 `IChatClient`、无效配置在启动验证时报具体键名。

## Task 2：所有 Agent 接入同一 IChatClient

**Files:**
- 修改：`src/Program.cs`
- 修改：`src/services/AgUiEndpoints.cs`
- 修改：`src/services/MultiAgentOrchestrator.cs`
- 修改：`src/services/RecommendationService.cs`
- 修改：`tests/unit/RecommendationServiceTests.cs`
- 创建：`tests/unit/AgentProviderWiringTests.cs`

**Interfaces:**
- 构造函数注入 `IChatClient`，不得在 Agent service 内读取 `AZURE_OPENAI_*` 或构造 Azure SDK client。
- AG-UI 从请求的 `IServiceProvider` 解析同一 singleton `IChatClient` 后创建指定 instructions/tools 的 `AIAgent`。

- [ ] **步骤 1：更新 RecommendationService 行为测试**

修改 `RecommendationServiceTests`：注入 mock `IChatClient`/可替代 Agent factory，分别测试成功的模型输出、非取消 provider 异常、显式 fallback 标记及取消传播。Provider 异常不得被测试当作有效模型答复。

- [ ] **步骤 2：运行测试确认当前 Azure 初始化与测试替身不兼容**

运行 `dotnet test tests\Tests.csproj --filter FullyQualifiedName~RecommendationServiceTests`，记录缺少构造函数依赖/现有未标记 fallback 的失败。

- [ ] **步骤 3：迁移调用路径**

将 `Program.cs` 中 singleton `AIAgent` 改为由注册的 `IChatClient` 创建。将 `MultiAgentOrchestrator.GetChatClient` 中 Azure 配置与创建代码删除并注入客户端。将 `RecommendationService.GetOrCreateChatClient` 改为注入客户端。将 `AgUiEndpoints.CreateAgentForIdAsync` 的 Azure 配置读取改为解析客户端，同时保留 agentId 分支、prompts 和工具集。

- [ ] **步骤 4：明确 provider 失败和 fallback**

在 `RecommendationService` 中仅捕获需转成服务错误的 provider 异常，写结构化日志；若保留现有煤/默认建议 fallback，结果必须标记 `FallbackUsed = true` 并由 API 暴露，不能发出 success-shaped 的模型结果。OperationCanceledException 继续向上传递。AG-UI 的失败响应保留现有 `RUN_FINISHED status=failed` 形状。

- [ ] **步骤 5：运行 AI 目标测试**

运行 `dotnet test tests\Tests.csproj --filter "FullyQualifiedName~RecommendationServiceTests|FullyQualifiedName~AgentProviderWiringTests|FullyQualifiedName~ChatClientRegistrationTests"`。预期成功路径、工具 Agent 创建、失败形状和取消行为均通过。

## Task 3：验证自托管模型并延后移除 Azure package

**Files:**
- 修改：`src/src.csproj`
- 修改：`src/Directory.Packages.props`
- 修改：`tests/integration/` 下新建 `LocalAiAgentTests.cs`
- 修改：`README.md`
- 检查：`src/services/AgUiEndpoints.cs`、`src/services/MultiAgentOrchestrator.cs`、`src/services/RecommendationService.cs`

- [ ] **步骤 1：添加本地模型 opt-in 集成测试**

`LocalAiAgentTests` 在 `LOCAL_AI_TESTS=1` 时访问由 `LLM_BASE_URL`、`LLM_MODEL_NAME`、`LLM_API_KEY` 映射来的 `AI:*` 设置，提交一项要求工具调用的 prompt，并验证有最终内容/正常结束事件；未设置 flag 时测试显式跳过，普通 CI 不下载模型。

- [ ] **步骤 2：运行本地模型集成测试（开发者环境）**

启动或访问用户控制的 OpenAI-compatible self-hosted inference server 后运行 `LOCAL_AI_TESTS=1` 的目标测试。预期至少一个项目 Agent 的工具调用完成且响应有内容；无模型服务时得到明确连接错误，不返回静默 fallback 成功。

- [ ] **步骤 3：移除旧 Azure OpenAI package 和配置说明**

README 同时记录 `Runtime:Mode=SelfHosted` 与 Azure legacy mode。只有完整 Drasi/Aspire/API/SSE/SignalR/Agent 兼容性闸门已记为 Passed 后，才从 `src/src.csproj` 删除 `Azure.AI.OpenAI`，并同步删除 `Directory.Packages.props` 中不再使用的 Azure OpenAI package 固定项；此前不得执行 package cleanup。

- [ ] **步骤 4：检查 Azure OpenAI 残留并跑测试**

闸门通过并获准清理后，运行 `rg -n "AzureOpenAIClient|AZURE_OPENAI_|Azure.AI.OpenAI" src tests`，预期无 SelfHosted runtime path 残留；Azure legacy package 删除须与单独批准的 cutover commit 同步。运行 `dotnet build SantaDigitalShowcae25.sln` 和 `dotnet test tests\Tests.csproj`，预期通过。

## Task 4：提交本地 AI 工作流

- [ ] 确认 opt-in local model integration test 的模型端点及 model 名与 AppHost 计划的 `AI:Endpoint`、`AI:Model` 配置一致。
- [ ] 使用提交说明 `feat: support local OpenAI-compatible agent provider`，只提交此计划对应文件。
