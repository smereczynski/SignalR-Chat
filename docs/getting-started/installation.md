# Installation Guide

This guide covers setting up SignalR Chat with full Azure resources for complete functionality including persistence, scalability, and email OTP delivery.

## Overview

SignalR Chat can run in two modes:

| Mode | Setup Time | Azure Required | Persistence | Use Case |
|------|-----------|----------------|-------------|----------|
| **In-Memory** | 5 minutes | ❌ No | ❌ No | Quick testing, UI development |
| **Azure (Full)** | 30-45 minutes | ✅ Yes | ✅ Yes | Production, full feature testing |

**This guide covers Azure (Full) mode**. For in-memory mode, see [Quickstart Guide](quickstart.md).

---

## Prerequisites

### Required

1. **Azure Subscription** ([Create free account](https://azure.microsoft.com/free/))
   - Contributor role (minimum)
   - Owner role (recommended for RBAC)

2. **Azure CLI** (v2.50.0+)
   ```bash
   # Install Azure CLI
   # macOS
   brew install azure-cli
   
   # Windows
   winget install Microsoft.AzureCLI
   
   # Linux
   curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
   
   # Verify installation
   az --version
   ```

3. **.NET 10.0 SDK**
   ```bash
  # Download from https://dotnet.microsoft.com/download/dotnet/10.0
   
   # Verify installation
   dotnet --version
   ```

4. **Git**
   ```bash
   # Verify installation
   git --version
   ```

### Optional (Recommended for Development)

- **Visual Studio Code** with C# Dev Kit extension
- **Node.js 18+** (for frontend build tools)
- **jq** (for parsing JSON in scripts)
  ```bash
  brew install jq  # macOS
  sudo apt install jq  # Linux
  ```

---

## Installation Methods

Choose your preferred method:

### Method 1: Automated (GitHub Actions) - Recommended

**Best for**: Production deployments, team environments

**Steps**:
1. Fork the repository
2. Configure GitHub secrets and variables
3. Run deployment workflow
4. Verify deployment

[Jump to Automated Installation](#automated-installation-github-actions)

### Method 2: Manual (Azure CLI + Bicep)

**Best for**: Learning infrastructure, custom modifications

**Steps**:
1. Clone repository
2. Run Bicep deployment locally
3. Get connection strings
4. Configure `.env.local`

[Jump to Manual Installation](#manual-installation-azure-cli--bicep)

---

## Automated Installation (GitHub Actions)

### Step 1: Fork Repository

1. Go to [SignalR-Chat repository](https://github.com/smereczynski/SignalR-Chat)
2. Click **Fork** button (top-right)
3. Choose your account/organization
4. **Uncheck** "Copy the main branch only" (to get all branches)
5. Click **Create fork**

### Step 2: Configure Azure Credentials

**Create Service Principal** for GitHub Actions:

```bash
# Login to Azure
az login

# Set subscription
az account set --subscription "<your-subscription-id>"

# Create service principal with OIDC federation
az ad sp create-for-rbac \
  --name "SignalRChat-GitHub-OIDC" \
  --role contributor \
  --scopes /subscriptions/<subscription-id> \
  --sdk-auth

# Output (save this JSON):
{
  "clientId": "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX",
  "clientSecret": "XXXXXXXXXXXXXXXXXXXXXXXXXXXX",
  "subscriptionId": "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX",
  "tenantId": "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX",
  ...
}
```

### Step 3: Configure GitHub Secrets

**In your forked repository**:

1. Go to **Settings** → **Secrets and variables** → **Actions**
2. Click **New repository secret**
3. Add these secrets:

| Secret Name | Value | How to Get |
|-------------|-------|------------|
| `AZURE_CLIENT_ID` | Service principal client ID | From step 2 output |
| `AZURE_TENANT_ID` | Azure AD tenant ID | From step 2 output |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID | From step 2 output |
| `OTP_PEPPER` | Random 32-byte string | `openssl rand -base64 32` |

**⚠️ Important**: Generate a **unique** `OTP_PEPPER` for each environment (dev/staging/prod).

### Step 4: Configure Environment Variables

**For each environment** (dev, staging, prod):

1. Go to **Settings** → **Environments**
2. Create environment: `dev` (or `staging`, `prod`)
3. Click **Add variable** and configure:

| Variable Name | dev Example | staging Example | prod Example |
|--------------|-------------|-----------------|--------------|
| `BICEP_BASE_NAME` | `signalrchat` | `signalrchat` | `signalrchat` |
| `BICEP_LOCATION` | `polandcentral` | `polandcentral` | `polandcentral` |
| `BICEP_VNET_ADDRESS_PREFIX` | `10.0.0.0/26` | `10.1.0.0/26` | `10.2.0.0/26` |
| `BICEP_APP_SERVICE_SUBNET_PREFIX` | `10.0.0.0/27` | `10.1.0.0/27` | `10.2.0.0/27` |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | `10.0.0.32/27` | `10.1.0.32/27` | `10.2.0.32/27` |
| `BICEP_ACS_DATA_LOCATION` | `Europe` | `Europe` | `Europe` |

**⚠️ Critical**: Each environment **MUST** use different VNet address spaces to avoid conflicts.

### Step 5: Run Deployment Workflow

1. Go to **Actions** tab in your fork
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow** (right side)
4. Select environment: `dev`
5. Select action: `deploy`
6. Click **Run workflow** button

**Deployment progress**:
```
✓ Checkout code
✓ Azure Login
✓ What-If Analysis (preview changes)
✓ Deploy Infrastructure (~20-30 minutes)
  - Virtual Network (2 subnets)
  - Cosmos DB (3 containers)
  - Redis
  - SignalR Service
  - Communication Services
  - App Service
  - Monitoring (App Insights, Log Analytics)
✓ Post-Deployment Validation
✓ Database Seeding (automatic on first startup)
```

### Step 6: Verify Deployment

After workflow completes (~25 minutes), check the output:

```
✅ Deployment successful!
App URL: https://signalrchat-dev-polandcentral.azurewebsites.net
Environment: dev
Location: polandcentral
```

**Test the application**:
1. Open the App URL in browser
2. Login as **alice** (check email for OTP)
3. Join **General** room
4. Send a message
5. Open incognito window, login as **bob**
6. See real-time message delivery

---

## Manual Installation (Azure CLI + Bicep)

### Step 1: Clone Repository

```bash
# Clone repository
git clone https://github.com/smereczynski/SignalR-Chat.git
cd SignalR-Chat
```

### Step 2: Login to Azure

```bash
# Login
az login

# Set subscription
az account set --subscription "<subscription-id>"

# Verify
az account show
```

### Step 3: Create Resource Group

```bash
# Choose environment: dev, staging, or prod
ENVIRONMENT="dev"
LOCATION="polandcentral"

# Create resource group
az group create \
  --name "rg-signalrchat-${ENVIRONMENT}-weu" \
  --location "$LOCATION"
```

### Step 4: Deploy Infrastructure with Bicep

```bash
# Navigate to Bicep directory
cd infra/bicep

# Deploy infrastructure
az deployment group create \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --template-file main.bicep \
  --parameters @main.parameters.${ENVIRONMENT}.bicepparam \
  --parameters otpPepper="$(openssl rand -base64 32)"

# Deployment takes ~20-30 minutes
# Output will show connection strings and URLs
```

**Expected resources deployed**:
- ✅ Virtual Network with 2 subnets
- ✅ Cosmos DB account (chat database, 3 containers)
- ✅ Azure Cache for Redis
- ✅ Azure SignalR Service
- ✅ Azure Communication Services
- ✅ App Service Plan + App Service
- ✅ Application Insights + Log Analytics
- ✅ Private Endpoints (Cosmos, Redis, SignalR)

### Step 5: Get Connection Strings

```bash
# Set variables
RG_NAME="rg-signalrchat-${ENVIRONMENT}-weu"
COSMOS_NAME="cdb-signalrchat-${ENVIRONMENT}-weu"
REDIS_NAME="redis-signalrchat-${ENVIRONMENT}-weu"
SIGNALR_NAME="sigr-signalrchat-${ENVIRONMENT}-weu"
ACS_NAME="acs-signalrchat-${ENVIRONMENT}"

# Get Cosmos DB connection string
COSMOS_CONN=$(az cosmosdb keys list \
  --name "$COSMOS_NAME" \
  --resource-group "$RG_NAME" \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" \
  --output tsv)

# Get Redis connection string
REDIS_KEY=$(az redis list-keys \
  --name "$REDIS_NAME" \
  --resource-group "$RG_NAME" \
  --query "primaryKey" \
  --output tsv)
REDIS_HOST="${REDIS_NAME}.redis.cache.windows.net"
REDIS_CONN="${REDIS_HOST}:6380,password=${REDIS_KEY},ssl=True,abortConnect=False"

# Get SignalR connection string
SIGNALR_CONN=$(az signalr key list \
  --name "$SIGNALR_NAME" \
  --resource-group "$RG_NAME" \
  --query "primaryConnectionString" \
  --output tsv)

# Get Communication Services connection string
ACS_CONN=$(az communication list-key \
  --name "$ACS_NAME" \
  --resource-group "$RG_NAME" \
  --query "primaryConnectionString" \
  --output tsv)

# Get Application Insights connection string
APPINSIGHTS_CONN=$(az monitor app-insights component show \
  --app "ai-signalrchat-${ENVIRONMENT}-weu" \
  --resource-group "$RG_NAME" \
  --query "connectionString" \
  --output tsv)
```

### Step 6: Configure `.env.local`

Create `.env.local` in the repository root and set the required connection strings and settings.

See the canonical reference: **[Configuration Guide](configuration.md)**.

**⚠️ Important**: 
- `.env.local` is in `.gitignore` - never commit it
- Update `ACS_EMAIL_FROM` with your verified sender email

### Step 7: Run Locally

```bash
# Load .env.local and run
bash -lc "set -a; source .env.local; dotnet run --project ./src/Chat.Web --urls=https://localhost:5099"

# Or use VS Code task: "Run Chat (Azure local env)"
```

**First startup will automatically seed database**:
```
info: Chat.Web.Services.DataSeederService[0]
      Checking if database needs seeding...
info: Chat.Web.Services.DataSeederService[0]
      Database is empty - starting seed process
info: Chat.Web.Services.DataSeederService[0]
      ✓ Database seeding completed successfully
```

---

## Azure Resources Created

### Resource Overview

| Resource | Type | Purpose | Cost (dev) |
|----------|------|---------|-----------|
| Virtual Network | Networking | Network isolation | Free |
| Cosmos DB | NoSQL Database | Messages, rooms, users | ~$10/month |
| Redis | Cache | OTP storage, rate limiting | ~$15/month |
| SignalR Service | Real-time | WebSocket connections | Free tier |
| Communication Services | Email/SMS | OTP delivery | Pay-per-use |
| App Service | Web Hosting | Application hosting | ~$13/month |
| Application Insights | Monitoring | Telemetry, logs | ~$5/month |
| Log Analytics | Logging | Centralized logs | Included |

**Total estimated cost (dev)**: **$40-50/month**

### Cosmos DB Containers

| Container | Partition Key | Purpose | Indexed Fields |
|-----------|--------------|---------|----------------|
| `messages` | `/roomId` | Chat messages | timestamp, userId |
| `rooms` | `/id` | Chat rooms | name |
| `users` | `/id` | User profiles | userName, email |

### Network Configuration

**Virtual Network**: 10.x.0.0/26 (64 IPs)
- **Subnet 1** (App Service): 10.x.0.0/27 (32 IPs)
  - Delegated to `Microsoft.Web/serverFarms`
  - Used for VNet integration
- **Subnet 2** (Private Endpoints): 10.x.0.32/27 (32 IPs)
  - Private endpoints for Cosmos DB, Redis, SignalR
  - Network isolation from internet

---

## Configuration

### Application Settings

Configuration is documented in one place to avoid drift:

- **[Configuration Guide](configuration.md)**

### Environment Variables

For the full, up-to-date list of environment variables and how they map to configuration, see:

- **[Configuration Guide](configuration.md)**

---

## Email OTP Configuration

To send OTP codes via email (instead of terminal output):

### Step 1: Verify Email Domain in Azure Communication Services

1. Go to **Azure Portal** → **Communication Services**
2. Select your Communication Services resource
3. Go to **Email** → **Domains**
4. Click **Add domain**
5. Choose:
   - **Azure Managed Domain** (free, quick setup) - OR -
   - **Custom Domain** (requires DNS configuration)

### Step 2: Configure Sender Email

```bash
# Get verified sender email from ACS
az communication email domain list \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --email-service-name "acs-signalrchat-${ENVIRONMENT}" \
  --query "[0].fromSenderDomain" \
  --output tsv

# Example output: yourdomain.azurecomm.net
```

### Step 3: Update Configuration

**Local development** (`.env.local`):
```bash
ACS_EMAIL_FROM="DoNotReply@yourdomain.azurecomm.net"
```

**Azure App Service**:
```bash
az webapp config appsettings set \
  --name "signalrchat-${ENVIRONMENT}-app" \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --settings "Acs__EmailFrom=DoNotReply@yourdomain.azurecomm.net"
```

### Step 4: Test Email Delivery

1. Run application
2. Login as **alice**
3. Check email for OTP code
4. Enter code to complete authentication

---

## Health Checks

Verify deployment health:

### Application Health

```bash
# Get app URL
APP_URL=$(az webapp show \
  --name "signalrchat-${ENVIRONMENT}-app" \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --query "defaultHostName" \
  --output tsv)

# Liveness probe (unauthenticated)
curl -i "https://$APP_URL/healthz"
# Expected: HTTP/1.1 200 OK

# Detailed health (requires authentication)
curl -i "https://$APP_URL/health"
# Returns JSON with component statuses
```

### Component Health

```bash
# Cosmos DB
az cosmosdb show \
  --name "cdb-signalrchat-${ENVIRONMENT}-weu" \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --query "{Name:name, Status:provisioningState}" \
  --output table

# Redis
az redis show \
  --name "redis-signalrchat-${ENVIRONMENT}-weu" \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --query "{Name:name, Status:provisioningState, State:redisVersion}" \
  --output table

# SignalR
az signalr show \
  --name "sigr-signalrchat-${ENVIRONMENT}-weu" \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu" \
  --query "{Name:name, Status:provisioningState, State:hostName}" \
  --output table
```

---

## Troubleshooting

### Common Installation Issues

#### Issue: "Resource name already exists"

**Cause**: Cosmos DB, App Service, or Communication Services names must be globally unique.

**Solution**: Modify `BICEP_BASE_NAME` variable to use a different base name:
```bash
# In GitHub: Update environment variable BICEP_BASE_NAME
# Or locally: Edit infra/bicep/main.parameters.<env>.bicepparam
```

#### Issue: "VNet address space overlaps"

**Cause**: Using same VNet address space for multiple environments.

**Solution**: Use different address spaces per environment:
- Dev: `10.0.0.0/26`
- Staging: `10.1.0.0/26`
- Prod: `10.2.0.0/26`

#### Issue: "Cosmos DB deployment slow"

**Cause**: Multi-region replication (production) takes 30+ minutes.

**Solution**: This is expected. Production deployments with zone redundancy are slower.

#### Issue: "Database not seeding on startup"

**Cause**: App started with existing data or seeding failed.

**Solution**: Check App Service logs:
```bash
az webapp log tail \
  --name "signalrchat-${ENVIRONMENT}-app" \
  --resource-group "rg-signalrchat-${ENVIRONMENT}-weu"

# Look for:
# "Checking if database needs seeding..."
# "Database is empty - starting seed process" OR
# "Database already contains data - skipping seed"
```

#### Issue: "Can't login - OTP not received"

**Cause**: Azure Communication Services email domain not verified.

**Solution**: 
1. Verify email domain in Azure Portal
2. Check `ACS_EMAIL_FROM` configuration
3. Check spam folder
4. View logs for email send errors

### Getting Help

- **Documentation**: [docs/README.md](../README.md)
- **FAQ**: [FAQ](../reference/faq.md)
- **GitHub Issues**: [Open an issue](https://github.com/smereczynski/SignalR-Chat/issues)
- **Discussions**: [Ask a question](https://github.com/smereczynski/SignalR-Chat/discussions)

---

## Next Steps

- **[Configuration Guide](configuration.md)** - Customize application settings
- **[Deployment Guide](../deployment/README.md)** - Deploy to production
- **[Local Development](../development/local-setup.md)** - Set up development environment
- **[Monitoring Guide](../operations/monitoring.md)** - Set up observability

---

**✅ Installation complete!** Your SignalR Chat application is now running with full Azure resources.

**Production deployment?** See [Production Checklist](../deployment/production-checklist.md) for security hardening and performance optimization.
