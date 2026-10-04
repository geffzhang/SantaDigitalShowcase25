# 🎅 Santa's Digital Elves – Wishlist Detection & Gift Report

Santa's Digital Elves is an event-driven demo application that detects wishlist and profile updates with [Drasi](https://drasi.io/) and generates intelligent gift recommendations. It supports both its Azure deployment and a SelfHosted local runtime managed by .NET Aspire.

[Video - Santa's Digital Showcase (Drasi + Microsoft Agent Framework)](https://youtu.be/3G7Dg_VI27M)

## 🎯 What This Solution Does

- **Real-time Event Detection**: Uses Drasi for continuous query processing on event streams
- **Wishlist Management**: Children can submit and update their Christmas wishlists
- **AI-Powered Analysis**: Azure OpenAI in Azure deployments, or a user-controlled OpenAI-compatible model endpoint in SelfHosted mode
- **Two runtime options**: Azure Container Apps and Drasi on AKS, or .NET Aspire with PostgreSQL and standalone Drasi Server

## 🛠️ Technology Stack

| Layer                | Technology                                                 |
| -------------------- | ---------------------------------------------------------- |
| **Backend**          | C# / .NET 9 with ASP.NET Core Minimal APIs                 |
| **Frontend**         | TypeScript with Vite + React                               |
| **Database**         | Azure Cosmos DB (Azure) or PostgreSQL 18.3 (SelfHosted)    |
| **Event Processing** | Drasi on AKS/Event Hubs (Azure) or Drasi Server/PostgreSQL CDC (SelfHosted) |
| **AI Framework**     | Azure OpenAI or a user-controlled OpenAI-compatible endpoint |
| **Infrastructure**   | Azure Container Apps/Key Vault/AKS or .NET Aspire/Docker   |
| **Scripting**        | PowerShell for deployment and automation                   |

![🎅 Santa's Workshop Dashboard](overview.jpg)

## 📋 Prerequisites

For Azure deployment, ensure you have the following installed:

- **Azure CLI** (`az`) – [Install guide](https://learn.microsoft.com/cli/azure/install-azure-cli?view=azure-cli-latest&WT.mc_id=AZ-MVP-5004796)
- **Azure Developer CLI** (`azd`) – [Install guide](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd?tabs=winget-windows%2Cbrew-mac%2Cscript-linux&pivots=os-windows&WT.mc_id=AZ-MVP-5004796)
- **.NET SDK 9** – [Download](https://dotnet.microsoft.com/download/dotnet/9.0?WT.mc_id=AZ-MVP-5004796)
- **Node.js 18+** – [Download](https://nodejs.org/)
- **Docker** (optional, for local container builds)
- **kubectl** – [Install guide](https://kubernetes.io/docs/tasks/tools/)
- **Drasi CLI** – [Install guide](https://drasi.io/)

## 🧰 Run SelfHosted locally (no Azure or kind)

The Aspire AppHost starts the API, PostgreSQL 18.3, standalone Drasi Server 0.2.3, and the Vite frontend. Docker, the .NET 9 SDK, and Node.js 18+ are required. This path does not require Azure credentials, `kind`, Kubernetes, Dapr, or the Drasi CLI; the Azure runtime remains available below.

Configure the user-controlled OpenAI-compatible model endpoint and the local PostgreSQL password as AppHost user secrets. Do not put secret values in the repository:

```powershell
dotnet user-secrets set "LLM_BASE_URL" "https://<your-model-host>/v1" --project AppHost
dotnet user-secrets set "LLM_MODEL_NAME" "<model-name>" --project AppHost
dotnet user-secrets set "LLM_API_KEY" "<model-api-key>" --project AppHost
dotnet user-secrets set "Parameters:postgres-password" "<local-postgres-password>" --project AppHost
```

Start the complete local stack:

```powershell
dotnet run --project AppHost\AppHost.csproj
```

The default loopback endpoints are API `http://localhost:8081`, PostgreSQL `localhost:5433`, and Drasi Server `http://localhost:8080`. PostgreSQL uses a persistent Aspire data volume. To validate the live PostgreSQL CDC → Drasi query → HTTP reaction → persisted notification → SSE path, run:

```powershell
Invoke-Pester -Script tests\scripts\DrasiServerConfig.Tests.ps1 -EnableExit
Invoke-Pester -Script tests\scripts\validate-selfhosted-drasi.Tests.ps1 -EnableExit
.\tests\scripts\validate-selfhosted-drasi.ps1
dotnet test tests\Tests.csproj --no-restore
```

## 🚀 Quick Start Deployment

### 1. Clone and Navigate

```powershell
# Clone the repository (or copy this folder to your own repo)
cd SantaDigitalShowcae25
```

### 2. Authenticate with Azure

```powershell
az login
azd auth login
```

### 3. Create Environment and Deploy

```powershell
# Create a new azd environment
azd env new <your-environment-name>

# Deploy infrastructure and all services
azd up

# Verify deployment
.\scripts\test-demo-readiness.ps1
```

### 4. Access the Application

After deployment completes, access your application:

```powershell
# Get the application URL
$apiUrl = azd env get-value apiHost
Start-Process "https://$apiUrl"
```

## 🏗️ Project Structure

```
SantaDigitalShowcae25/
├── src/                    # .NET 9 backend API
│   ├── Middleware/         # ASP.NET Core middleware
│   ├── Realtime/           # Real-time event handling
│   ├── lib/                # Shared libraries
│   ├── models/             # Domain models and DTOs
│   ├── services/           # Business logic and API endpoints
│   └── Program.cs          # Application entry point
├── frontend/               # Vite + React frontend
│   ├── src/                # React components
│   └── public/             # Static assets
├── tests/                  # Test projects
│   ├── unit/               # xUnit unit tests
│   ├── integration/        # Integration tests
│   └── contract/           # Contract tests
├── drasi/                  # Drasi event graph configuration
│   ├── manifests/          # Kubernetes manifests for Drasi
│   └── resources/          # Drasi resource definitions
├── infra/                  # Bicep infrastructure as code
│   └── modules/            # Modular Bicep templates
├── scripts/                # PowerShell automation scripts
├── azure.yaml              # Azure Developer CLI configuration
├── Dockerfile.multi        # Multi-stage Docker build
└── SantaDigitalShowcae25.sln  # Visual Studio solution file
```

## 💻 Local Development

### Backend (.NET)

```powershell
# Restore dependencies
dotnet restore src/src.csproj

# Build the solution
dotnet build src/src.csproj

# Run the API locally (port 8080)
$env:ASPNETCORE_URLS = "http://localhost:8080"
dotnet run --project src

# Run tests
dotnet test tests/Tests.csproj
```

### Frontend (React/Vite)

```powershell
cd frontend
npm install
npm run dev      # Development server with hot reload
npm run build    # Production build
```

### Full Solution Build

```powershell
dotnet build SantaDigitalShowcae25.sln
dotnet test
```

## 🔌 API Endpoints

### Health Endpoints

| Endpoint     | Purpose             |
| ------------ | ------------------- |
| `/healthz`   | Liveness check      |
| `/readyz`    | Readiness check     |
| `/api/pingz` | Diagnostics payload |

### Core API Endpoints (v1)

| Endpoint                               | Method    | Description            |
| -------------------------------------- | --------- | ---------------------- |
| `/api/v1/children`                     | GET       | List all children      |
| `/api/v1/children/{id}`                | GET       | Get child details      |
| `/api/v1/children/{id}/wishlist-items` | GET, POST | Manage wishlist items  |
| `/api/v1/reports`                      | GET       | List reports           |
| `/api/v1/elf-agents/{agentId}/run`     | POST      | Run AI elf agent (SSE) |
| `/api/v1/drasi/insights`               | GET       | Get Drasi insights     |
| `/api/v1/copilot/chat`                 | POST      | Chat with AI (SSE)     |

## ⚙️ Configuration

### Environment Variables

The application uses Azure Key Vault for secrets. Key configuration values:

| Variable          | Description              |
| ----------------- | ------------------------ |
| `KEYVAULT_URI`    | Azure Key Vault URI      |
| `COSMOS_ENDPOINT` | Cosmos DB endpoint       |
| `OPENAI_ENDPOINT` | Azure OpenAI endpoint    |
| `EVENTHUB_FQDN`   | Event Hub namespace FQDN |

### Drasi Configuration

Drasi resources are managed via the Drasi CLI:

```powershell
# Set Drasi environment
drasi env kube -n drasi-system

# Apply Drasi resources
drasi apply -f drasi/manifests/drasi-resources.yaml
```

## 🔧 Troubleshooting

### Common Issues

1. **API returns 404**: Container App may still be using bootstrap image

   ```powershell
   azd deploy api
   ```

2. **Drasi pods in CrashLoopBackOff**: Run the fix script

   ```powershell
   $rg = azd env get-value AZURE_RESOURCE_GROUP
   $env = azd env get-value AZURE_ENV_NAME
   .\scripts\fix-drasi-deployment.ps1 -ResourceGroup $rg -Project "santadigitalshowcase" -Env $env
   ```

3. **Frontend shows 404 on API calls**: Ensure the Container App is properly deployed
   ```powershell
   azd deploy api
   ```

### Validation Script

Run the demo readiness check to validate your deployment:

```powershell
.\scripts\test-demo-readiness.ps1
```

## 📦 Deployment Options

### Azure Developer CLI (Recommended)

```powershell
azd up              # Full deployment
azd deploy api      # Deploy only backend
azd deploy drasi    # Deploy only Drasi resources
azd down            # Tear down all resources
```

### Manual Bicep Deployment

```powershell
$project = "santadigitalshowcase"
$env = "dev"
$rg = "${project}-${env}-rg"
$loc = "eastus"

az group create -n $rg -l $loc
az deployment group create -g $rg -f ./infra/main.bicep -p project=$project env=$env
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is provided as a demo/sample application for the Festive Tech Calendar 2025.

## 🔗 Resources

- [Drasi Documentation](https://drasi.io/docs/)
- [Azure Container Apps](https://learn.microsoft.com/azure/container-apps/)
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/)
- [.NET 9 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [Festive Tech Calendar](https://festivetechcalendar.com/)
