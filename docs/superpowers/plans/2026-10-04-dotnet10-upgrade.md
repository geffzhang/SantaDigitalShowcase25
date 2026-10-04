# .NET 10 Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade every .NET project and both container publishing paths to .NET 10 while preserving Azure and SelfHosted behavior.

**Architecture:** Keep the current solution and runtime topology intact, changing project TFMs, framework-coupled packages, .NET container images, and active version guidance only. Retain Aspire 13.6.0 and unrelated dependencies unless validation proves a compatibility blocker.

**Tech Stack:** .NET SDK 10.0.401, .NET 10, EF Core 10, Npgsql EF Core provider 10, .NET Aspire 13.6.0, Docker, xUnit, Pester, PostgreSQL, Drasi.

## Global Constraints

- Every .NET project targets `net10.0`.
- SDK and ASP.NET runtime container images use .NET 10.
- EF Core and its provider must use a compatible stable 10.x combination.
- Aspire AppHost SDK and Hosting packages stay on one version; keep 13.6.0 unless compatibility validation requires upgrading the entire Aspire package group.
- Do not use prerelease packages or perform an unrelated dependency refresh.
- Preserve both Azure and SelfHosted deployment paths and existing application behavior.
- Do not add credentials to source files, logs, or container build arguments.

---

## File Map

- `src/src.csproj` — API target framework and production dependencies.
- `tests/Tests.csproj` — test target framework and EF Core/ASP.NET test dependencies.
- `AppHost/AppHost.csproj` — Aspire AppHost target framework and aligned Aspire package group.
- `Dockerfile.multi` — SDK and runtime images for the multi-stage container publish path.
- `src/Dockerfile` — SDK and runtime images for the API container publish path.
- `README.md` — active .NET stack, SDK prerequisites, SelfHosted prerequisite, and documentation link.
- `docs/guides/drasi-self-hosted-validation.md` — explicit .NET 10 SDK/container-runtime prerequisite for the current validation workflow.
- `SantaDigitalShowcae25.sln` — complete solution used for restore, build, and test commands; it is not expected to need edits.

## Task 1: Upgrade Project TFMs and Framework-Coupled Packages

**Files:**
- Modify: `src/src.csproj`
- Modify: `tests/Tests.csproj`
- Modify: `AppHost/AppHost.csproj`

**Interfaces:**
- Produces: all three solution projects target `net10.0`; the API and test projects use compatible .NET 10 framework packages; Aspire packages remain aligned at 13.6.0.

- [x] **Step 1: Record a baseline test result**

Run from the repository root:

```powershell
dotnet test .\SantaDigitalShowcae25.sln --configuration Release
```

Expected: the existing test suite completes successfully before the framework migration. Record any pre-existing skipped tests separately; do not change tests to mask a baseline failure.

- [x] **Step 2: Change all project target frameworks**

In each of `src/src.csproj`, `tests/Tests.csproj`, and `AppHost/AppHost.csproj`, replace:

```xml
<TargetFramework>net9.0</TargetFramework>
```

with:

```xml
<TargetFramework>net10.0</TargetFramework>
```

- [x] **Step 3: Align framework-dependent package versions**

In `src/src.csproj`, use these stable package versions, verified as the latest stable versions on 2026-10-04:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.12" />
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
```

In `tests/Tests.csproj`, align EF Core and ASP.NET Core test packages:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.12" />
```

Do not change the xUnit, test SDK, Testcontainers, or mocking package versions unless restore or test output identifies a specific incompatibility.

Keep `Aspire.AppHost.Sdk` and all four `Aspire.Hosting.*` references in `AppHost/AppHost.csproj` at `13.6.0`; these are aligned and are the latest stable package versions checked on 2026-10-04. Do not edit `src/Directory.Packages.props`; it does not centrally manage these explicit project versions.

- [x] **Step 4: Restore and build the full solution**

Run:

```powershell
dotnet restore .\SantaDigitalShowcae25.sln
dotnet build .\SantaDigitalShowcae25.sln --configuration Release --no-restore
```

Expected: restore succeeds, all projects compile for `net10.0`, and there are no new framework/package compatibility errors. Resolve actual failures before continuing; do not downgrade project TFMs to make restore pass.

- [x] **Step 5: Run all .NET tests**

Run:

```powershell
dotnet test .\SantaDigitalShowcae25.sln --configuration Release --no-build
```

Expected: all tests that passed at baseline still pass. PostgreSQL integration and migration tests continue to verify the existing persistence behavior.

- [x] **Step 6: Commit the project migration**

```powershell
git add -- .\src\src.csproj .\tests\Tests.csproj .\AppHost\AppHost.csproj
git commit -m "chore: upgrade projects to .NET 10" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Upgrade Container Images and Active Version Guidance

**Files:**
- Modify: `Dockerfile.multi`
- Modify: `src/Dockerfile`
- Modify: `README.md`
- Modify: `docs/guides/drasi-self-hosted-validation.md`

**Interfaces:**
- Consumes: the `net10.0` project outputs from Task 1.
- Produces: both container build paths use matching .NET 10 SDK/runtime image tags; the README gives .NET 10 prerequisites without removing Azure deployment instructions.

- [x] **Step 1: Update .NET container image tags**

In both `Dockerfile.multi` and `src/Dockerfile`, change the backend build image:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
```

to:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
```

Change the final ASP.NET runtime image:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
```

to:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
```

Keep frontend stages, copy paths, ports, and entrypoints unchanged.

- [x] **Step 2: Update active README .NET references**

In `README.md`:

- Change the technology stack from `.NET 9` to `.NET 10`.
- Change the SDK download URL to `https://dotnet.microsoft.com/download/dotnet/10.0` and label it `.NET SDK 10`.
- Change the SelfHosted prerequisite sentence to require the `.NET 10 SDK`.
- Change the resource link to `.NET 10 Documentation` at `https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview`.
- Keep the Azure and SelfHosted descriptions and setup instructions intact.
- In `docs/guides/drasi-self-hosted-validation.md`, add a short prerequisites section stating that the SelfHosted Aspire validation requires the .NET 10 SDK and Docker. Keep the documented commands and live-smoke assertions unchanged.

- [x] **Step 3: Build both container paths**

Run from the repository root:

```powershell
docker build -f .\Dockerfile.multi -t santa-digital-showcase:net10-multi .
docker build -f .\src\Dockerfile -t santa-digital-showcase:net10-src .
```

Expected: both builds finish successfully, including the frontend build, backend publish, and final image stages.

- [x] **Step 4: Commit the container and README migration**

```powershell
git add -- .\Dockerfile.multi .\src\Dockerfile .\README.md .\docs\guides\drasi-self-hosted-validation.md
git commit -m "chore: use .NET 10 container images" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Verify Aspire SelfHosted and Retained Azure Paths

**Files:**
- No source changes expected. If a validation exposes a product defect, stop and add a focused fix and regression test rather than weakening the assertions.
- Validate: `tests/scripts/DrasiServerConfig.Tests.ps1`
- Validate: `tests/scripts/validate-selfhosted-drasi.Tests.ps1`
- Validate: `tests/scripts/validate-selfhosted-drasi.ps1`

**Interfaces:**
- Consumes: project, dependency, Docker, and README updates from Tasks 1 and 2.
- Produces: evidence that .NET 10 builds preserve SelfHosted API/PostgreSQL/Drasi behavior and do not remove Azure build support.

- [x] **Step 1: Run the script contract tests**

Run:

```powershell
Invoke-Pester -Script .\tests\scripts\DrasiServerConfig.Tests.ps1 -EnableExit
Invoke-Pester -Script .\tests\scripts\validate-selfhosted-drasi.Tests.ps1 -EnableExit
```

Expected: both Pester suites pass without modifying their existing health, query, or reaction assertions.

- [x] **Step 2: Run the SelfHosted live smoke against the .NET 10 AppHost**

If an older Aspire AppHost is running, stop it gracefully before starting the upgraded AppHost; preserve the PostgreSQL data volume and do not delete or reset database state. Aspire's CLI bundle starts the AppHost with `--no-build` in Debug configuration when invoked through `dotnet run`, so build that configuration first:

```powershell
dotnet build .\AppHost\AppHost.csproj --configuration Debug
dotnet run --project .\AppHost\AppHost.csproj
```

Wait until the API `http://localhost:8081/healthz` and Drasi `http://localhost:8080/health` endpoints both respond successfully before running the smoke script in a second PowerShell session. The script performs each initial health check once; it is not a startup wait.

```powershell
.\tests\scripts\validate-selfhosted-drasi.ps1
```

Expected: the API and Drasi health checks succeed, query endpoints return valid responses, and a unique wishlist event reaches the persisted notification and live event path. If Docker or the AppHost cannot run in the environment, report this live check as blocked rather than treating container builds as equivalent evidence.

- [x] **Step 3: Re-run the complete Release build and .NET tests**

Run:

```powershell
dotnet build .\SantaDigitalShowcae25.sln --configuration Release
dotnet test .\SantaDigitalShowcae25.sln --configuration Release --no-build
```

Expected: the solution builds for .NET 10 and the full test suite passes with no new failures.

- [x] **Step 4: Check active configuration for stale .NET 9 references and secrets**

Review only active runtime/build files, not historical design records:

```powershell
rg -n 'net9\.0|\.NET 9|SDK 9|sdk:9\.0|aspnet:9\.0' .\README.md .\src\src.csproj .\tests\Tests.csproj .\AppHost\AppHost.csproj .\Dockerfile.multi .\src\Dockerfile
```

Expected: no .NET 9 references remain in those active files. Also inspect the final changes for accidentally added API keys or other secrets; all user secrets remain outside source and image-build arguments.

- [x] **Step 5: Confirm Azure configuration remains present**

Run:

```powershell
if (-not (Test-Path .\azure.yaml) -or -not (Test-Path .\infra)) { throw 'Azure deployment configuration is missing.' }
git status --short
git diff --check
```

Review the changed-file list and confirm the upgrade did not remove Azure deployment files or introduce unrelated changes. No Azure cloud deployment is required for this task.
