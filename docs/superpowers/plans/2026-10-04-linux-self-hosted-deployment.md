# Linux 自托管 Kubernetes 部署实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 通过 K3s 或标准 Kubernetes 清单部署不依赖 Azure 的 API、前端、PostgreSQL、本地模型及 Drasi，并提供持久化、备份和健康验证。

**Architecture:** 使用 Kustomize 管理一个 self-hosted base 和 Linux overlay。API/前端使用仓库 Docker build；数据库和模型作为自托管 workloads；Drasi 使用数据/Drasi计划选定的 provider manifests。Secret 使用 Kubernetes Secret 输入，不提交明文值。

**Tech Stack:** Docker、Kustomize、K3s/标准 Kubernetes、PostgreSQL、Drasi/Dapr、.NET 9 API。

## Global Constraints

- Linux runtime 不依赖 Azure 服务、Azure CLI、azd、Azure 身份或 Azure 端点。
- 应用镜像从当前代码构建；数据库和 Drasi 状态使用持久卷。
- K3s/标准 Kubernetes共用 Kustomize base；存储类、域名、密钥和模型 endpoint 使用 overlay/config。
- 集群内 readiness/liveness 检查不能依赖不可用的 Azure health endpoint。
- 备份/恢复命令必须可在宿主机执行并产生可验证的 PostgreSQL dump。

---

## 文件结构与边界

- 创建 `deploy/selfhosted/base/`：API、PostgreSQL、模型和 Drasi Kubernetes resources。
- 创建 `deploy/selfhosted/overlays/kind/`、`deploy/selfhosted/overlays/k3s/`、`deploy/selfhosted/overlays/standard/`：分别覆盖测试存储、K3s 存储类和标准集群存储类差异。
- 创建 `scripts/deploy-selfhosted.ps1`、`scripts/backup-selfhosted.ps1`、`scripts/restore-selfhosted.ps1`。
- 修改：`Dockerfile.multi`（仅当 PostgreSQL provider 要求的 build/runtime依赖或 health probe 需要）。
- 修改：`README.md`、`docs/README.md`：以 Linux 自托管步骤替换默认 Azure-only快速入门。
- 保留：`infra/main.bicep` 与 `azure.yaml` 作为标记清楚的旧 Azure 部署路径，不由本计划删除。

## Task 1：定义 self-hosted Kustomize 基础资源

**Files:**
- 创建：`deploy/selfhosted/base/kustomization.yaml`
- 创建：`deploy/selfhosted/base/api-deployment.yaml`
- 创建：`deploy/selfhosted/base/api-service.yaml`
- 创建：`deploy/selfhosted/base/postgres-statefulset.yaml`
- 创建：`deploy/selfhosted/base/postgres-service.yaml`
- 创建：`deploy/selfhosted/base/postgres-pvc.yaml`
- 创建：`deploy/selfhosted/base/model-deployment.yaml`
- 创建：`deploy/selfhosted/base/model-service.yaml`
- 创建：`deploy/selfhosted/overlays/k3s/kustomization.yaml`
- 创建：`deploy/selfhosted/overlays/kind/kustomization.yaml`
- 创建：`deploy/selfhosted/overlays/standard/kustomization.yaml`

**Interfaces:**
- API container listens on configured ASP.NET Core port and reads PostgreSQL from `ConnectionStrings__elves`.
- AI config uses `AI__Endpoint`, `AI__Model` and `AI__ApiKey`; Drasi URLs use the existing Drasi configuration contract.
- Sensitive values are generated/provided by deployer and excluded from tracked files.

- [ ] **步骤 1：添加 Kubernetes schema/contract 测试**

创建 `tests/scripts/SelfHostedManifest.Tests.ps1`，用 `kustomize build` 对三个 overlay 生成资源，并验证必须包含 API Deployment/Service、PostgreSQL StatefulSet/PVC、model Deployment/Service 和经兼容性计划验证的 Drasi manifests；检查生成输出不含 `azure.com`、`servicebus.windows.net`、`vault.azure.net` 端点。

- [ ] **步骤 2：运行 manifest 测试确认失败**

运行 `Invoke-Pester tests\scripts\SelfHostedManifest.Tests.ps1 -Output Detailed`，预期因目录和 overlays 不存在而失败。

- [ ] **步骤 3：实现 base 和两种 overlay**

API Deployment 使用 `docker build -f Dockerfile.multi -t santa-api:local .` 生成的镜像，配置 readiness `/readyz`、liveness `/healthz`。PostgreSQL 使用 StatefulSet + PVC，密码来自 Secret，不放 ConfigMap。Model Deployment 的 image/model 由 overlay 参数配置，不声明强制 GPU resource limit。K3s overlay使用 K3s默认可用 storage class；standard overlay要求部署者提供 `storageClassName` 参数。

- [ ] **步骤 4：校验生成 Kubernetes YAML**

运行 `kustomize build deploy/selfhosted/overlays/kind`、`kustomize build deploy/selfhosted/overlays/k3s` 和 `kustomize build deploy/selfhosted/overlays/standard`。预期 YAML 可生成且无 secret literal、无 Azure endpoint。运行 `kubectl apply --dry-run=client -k deploy/selfhosted/overlays/kind`，预期所有对象通过 schema 校验。

## Task 2：添加部署、备份及恢复命令

**Files:**
- 创建：`scripts/deploy-selfhosted.ps1`
- 创建：`scripts/backup-selfhosted.ps1`
- 创建：`scripts/restore-selfhosted.ps1`
- 修改：`.gitignore`
- 创建：`deploy/selfhosted/README.md`

- [ ] **步骤 1：先写 deploy/backup/restore 脚本测试**

Pester 覆盖：缺少 kubectl/kustomize 报具体错误；deploy 只接受 `kind`、`k3s` 或 `standard` overlay；Secret 输入文件不存在时停止；backup 在 PostgreSQL dump 失败时返回非零且不报告成功；restore 要求用户显式指定确认参数，不覆盖当前数据而不提示。

- [ ] **步骤 2：运行 Pester 确认脚本尚未实现**

运行 `Invoke-Pester tests\scripts\SelfHostedDeployment.Tests.ps1 -Output Detailed`，预期脚本/函数不存在导致失败。

- [ ] **步骤 3：实现命令与敏感文件保护**

`deploy-selfhosted.ps1` 验证 context、要求 Secret 输入，构建或加载 API 镜像，执行 `kubectl apply -k` 并逐一等待 PostgreSQL、API、模型及 Drasi readiness。`kind` overlay 仅用于测试并配置可重建的测试卷；不得在 K3s/standard overlay 中使用 `hostPath` 替代持久卷。`.gitignore` 忽略 `deploy/selfhosted/**/secret*.yaml`、`*.dump` 和临时凭据文件；README 只给 `secret.example.yaml`。

backup 使用 `kubectl exec` 将 `pg_dump --format=custom` 输出到带时间戳文件，检查命令退出码和文件长度；restore 使用 `pg_restore --clean --if-exists` 前要求 `-ConfirmDataReplacement` 并对目标 database 做显式确认。

- [ ] **步骤 4：运行脚本单测及命令 help**

运行 Pester 测试并执行 `pwsh -File scripts\deploy-selfhosted.ps1 -Help`、`pwsh -File scripts\backup-selfhosted.ps1 -Help`、`pwsh -File scripts\restore-selfhosted.ps1 -Help`。预期 usage 包含前置条件、overlay 名及 Secret 文件参数。

## Task 3：在本地 Kubernetes 做部署验收并更新文档

**Files:**
- 修改：`README.md`
- 修改：`docs/README.md`
- 修改：`deploy/selfhosted/README.md`
- 检查：`Dockerfile.multi`
- 检查：`drasi/resources/drasi-resources.yaml`

- [ ] **步骤 1：构建 self-hosted API 镜像**

运行 `docker build -f Dockerfile.multi -t santa-api:local .`。预期前端 Vite build、.NET publish 均成功，最终镜像包含静态前端并只需运行 `dotnet src.dll`。

- [ ] **步骤 2：在一次性 kind 集群验证 overlay**

将镜像加载至本地 kind；提供临时 Kubernetes Secret，运行 `pwsh -File scripts\deploy-selfhosted.ps1 -Overlay kind`。等待 deployments/statefulsets Ready。

- [ ] **步骤 3：验证持久化和端到端行为**

调用 API 写入 child/wishlist/recommendation，确认通过 Drasi 查询及 SSE/SignalR 输出后，删除并重建 API Pod，再查询记录仍存在。运行 backup 命令确认得到非零字节 dump；在一次性测试 namespace restore 后对比记录数。

- [ ] **步骤 4：更新快速入门和执行回归**

README 的默认快速入门必须先介绍 Aspire/kind 开发流程和 Linux self-hosted 部署；原 Azure azd 步骤标记为 legacy/optional。运行 `kustomize build` 三 overlays、Pester 部署测试、`dotnet build SantaDigitalShowcae25.sln` 与 `dotnet test tests\Tests.csproj`。

## Task 4：提交 Linux 部署工作流

- [ ] 确认 Pester、Kustomize build、Docker build、端到端数据重启和 backup/restore 检查均通过。
- [ ] 使用提交说明 `feat: add Linux self-hosted Kubernetes deployment`，只提交本计划新增/修改的部署、脚本和文档文件。
