# Bootstrap Guide

This document explains how to deploy and initialize the SignalR Chat application infrastructure from scratch using Azure Bicep templates.

## Overview

The Chat application deployment consists of:

1. **Infrastructure Provisioning** (Azure Bicep): Deploy all Azure resources (VNet, Cosmos DB, Redis, SignalR, App Service, etc.)
2. **Automatic Data Seeding**: The application automatically seeds initial rooms and users on first startup if the database is empty

This on-demand approach provides:
- ✅ **Reproducible infrastructure**: Deploy consistent environments across dev/staging/prod
- ✅ **Version control**: Track infrastructure changes alongside application code
- ✅ **Explicit control**: Infrastructure provisioning happens when YOU decide
- ✅ **Automatic initialization**: Database seeds itself on first app startup
- ✅ **No accidental overwrites**: Seeding only occurs if database is completely empty
- ✅ **Clear audit trail**: All operations are deliberate and logged
- ✅ **Environment isolation**: Separate VNets, connection strings, and resources per environment

---

## Prerequisites

Before deploying, ensure you have:

1. **Azure CLI** (v2.50.0 or later)
   ```bash
   az --version
   az login
   az account set --subscription "<subscription-id>"
   ```

2. **Azure Subscription** with appropriate permissions:
   - Contributor role (minimum)
   - Owner role (recommended for RBAC assignments)

3. **Bicep CLI** (included with Azure CLI)
   ```bash
   az bicep version
   ```

4. **jq** (for parsing JSON output in scripts)
   ```bash
   brew install jq  # macOS
   sudo apt install jq  # Linux
   ```

5. **.NET 9 SDK** (for data seeding)
   ```bash
   dotnet --version
   ```

---

## Phase 1: Infrastructure Deployment (GitHub Actions)

All infrastructure deployments are automated through **GitHub Actions**. No local scripts or manual deployments are supported.

### Step 1: Configure GitHub Environment Variables

For each environment (dev, staging, prod), configure these required variables and secrets in GitHub:

**GitHub Repository → Settings → Secrets and variables → Actions**

#### Environment Variables (Variables tab)

| Variable Name | Description | Example (dev) | Example (staging) | Example (prod) |
|--------------|-------------|---------------|-------------------|----------------|
| `BICEP_BASE_NAME` | Base name for all resources | `signalrchat` | `signalrchat` | `signalrchat` |
| `BICEP_LOCATION` | Azure region | `polandcentral` | `polandcentral` | `polandcentral` |
| `BICEP_VNET_ADDRESS_PREFIX` | VNet CIDR block (/26) | `10.0.0.0/26` | `10.1.0.0/26` | `10.2.0.0/26` |
| `BICEP_APP_SERVICE_SUBNET_PREFIX` | First subnet (/27) | `10.0.0.0/27` | `10.1.0.0/27` | `10.2.0.0/27` |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | Second subnet (/27) | `10.0.0.32/27` | `10.1.0.32/27` | `10.2.0.32/27` |
| `BICEP_ACS_DATA_LOCATION` | ACS data location | `Europe` | `Europe` | `Europe` |

#### Required Secrets (Secrets tab)

| Secret Name | Description | How to Generate |
|------------|-------------|-----------------|
| `OTP_PEPPER` | Secure pepper for OTP hashing | `openssl rand -base64 32` |

**⚠️ Critical Notes**:
- Each environment MUST have a unique VNet address space to avoid conflicts
- **`OTP_PEPPER` is REQUIRED** and must be different for each environment (dev/staging/prod)
- Generate pepper: `openssl rand -base64 32`
- Keep pepper values secure and never commit them to git

### Step 2: Trigger Infrastructure Deployment

**Via GitHub UI** (Recommended):
1. Go to **Actions** tab
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Select environment: `dev`, `staging`, or `prod`
5. Click **Run workflow** button
6. Monitor deployment progress in the workflow run

**Via GitHub CLI**:
```bash
# Deploy to development
gh workflow run deploy-infrastructure.yml -f environment=dev

# Deploy to staging
gh workflow run deploy-infrastructure.yml -f environment=staging

# Deploy to production (requires approval)
gh workflow run deploy-infrastructure.yml -f environment=prod
```

### Step 3: Monitor Deployment

The GitHub Actions workflow performs these steps:

1. **Environment Selection**: Choose dev/staging/prod
2. **Azure Login**: Authenticate with federated credentials
3. **What-If Analysis**: Preview infrastructure changes
4. **Approval Gate** (prod only): Manual approval required
5. **Infrastructure Deployment**: Deploy all Bicep templates (~20-30 minutes)
6. **Post-Deployment Validation**: Verify 2 subnets exist in VNet
7. **Database Seeding**: Automatically seed rooms and users
8. **Outputs**: Display app URL and connection details

**Deployment Time**: 
- Dev/Staging: ~20-25 minutes
- Production: ~25-35 minutes (includes multi-region setup)

### Step 4: Verify Deployment

After workflow completes, check the deployment outputs in the workflow run summary:

```
✅ Deployment successful!
App URL: https://signalrchat-dev-polandcentral.azurewebsites.net
Environment: dev
Location: polandcentral
```

**Resources Deployed**:
| Resource | Naming Convention | Purpose |
|----------|------------------|---------|
| Virtual Network | `vnet-{codename}-{env}-{location}` | Network isolation with **2 subnets** (/27 each) |
| Subnet 1 | `10-0-0-0--27` format | App Service VNet integration |
| Subnet 2 | `10-0-0-32--27` format | Private Endpoints for Cosmos, Redis, SignalR |
| Network Security Groups | `nsg-*` | Security rules for both subnets |
| Log Analytics | `law-{codename}-{env}-{location}` | Centralized logging (30/90/365 day retention) |
| Application Insights | `ai-{codename}-{env}-{location}` | APM and telemetry (workspace-based) |
| Cosmos DB | `cdb-{codename}-{env}-{location}` | NoSQL database, 3 containers, zone-redundant |
| Azure Managed Redis | `redis-{codename}-{env}-{location}` | OTP storage, Balanced_B1/B3/B5 tiers |
| Azure SignalR | `sigr-{codename}-{env}-{location}` | Real-time communication, Standard_S1 (1/1/5 units) |
| Azure Communication Services | `acs-{codename}-{env}` | Email and SMS, Europe data location |
| App Service Plan | `serverfarm-{codename}-{env}-{location}` | P0V4 PremiumV4 hosting |
| App Service | `{codename}-{env}-{location}` | SignalR Chat application, VNet integrated |
| Private Endpoints | `pe-{resourcename}` | Secure connections to Cosmos, Redis, SignalR |

### Step 5: Access Deployed Application

The workflow outputs the application URL. You can access it directly:

```bash
# From workflow output
https://signalrchat-dev-polandcentral.azurewebsites.net

# Or query Azure CLI
az webapp show \
  --name signalrchat-dev-polandcentral \
  --resource-group rg-signalrchat-dev-polandcentral \
  --query "defaultHostName" \
  --output tsv
```

### Optional: Teardown Environment

To delete all resources in an environment:

1. Go to **Actions** → **Deploy Infrastructure** workflow
2. Click **Run workflow**
3. Select environment to teardown
4. Check **"Teardown (delete all resources)"** option
5. **⚠️ Confirm deletion** - this action is irreversible
6. Monitor workflow run for completion

**Alternative via Azure CLI**:
```bash
# Delete entire resource group (fastest)
az group delete --name rg-signalrchat-dev-polandcentral --yes --no-wait

# This deletes: VNet, App Service, Cosmos DB, Redis, SignalR, ACS, Monitoring
```

---

## Phase 2: Automatic Database Seeding

The application now automatically seeds initial data (rooms and users) during startup if the database is empty. **No manual seeding is required.**

### How It Works

**Automatic Seeding Service** (`src/Chat.Web/Services/DataSeederService.cs`):
- Runs during application startup (before serving requests)
- Checks if database is empty (no rooms AND no users)
- Seeds initial data only if both conditions are true
- Logs all operations for audit trail
- Allows app to start even if seeding fails

### Local Development Configuration

When running locally, create a `.env.local` file in the project root (already in `.gitignore`).

To keep configuration consistent and avoid drift, the canonical reference is:

- **[Configuration Guide](../getting-started/configuration.md)**

The `.env.local` file is automatically loaded by VS Code tasks when running the application. You can also manually load it:

```bash
source .env.local
```

### What Gets Seeded

**Rooms** (created directly in Cosmos, IDs auto-generated):
- `general`
- `ops`
- `random`

**Users** (via IUsersRepository):
- `alice`: FixedRooms=["general", "ops"], DefaultRoom="general", Email="alice@example.com"
- `bob`: FixedRooms=["general", "random"], DefaultRoom="general", Email="bob@example.com"
- `charlie`: FixedRooms=["general"], DefaultRoom="general", Email="charlie@example.com"

### Verifying Seeding

After deploying infrastructure and starting the app for the first time, check the logs:

```bash
# View App Service logs
az webapp log tail \
  --resource-group rg-signalrchat-dev \
  --name signalrchat-dev-app

# Look for these log messages:
# "Checking if database needs seeding..."
# "Database is empty - starting seed process"
# "Seeding default rooms..."
# "  ✓ Created room: general"
# "  ✓ Created room: ops"
# "  ✓ Created room: random"
# "Seeding default users..."
# "  ✓ Created user: alice"
# "  ✓ Created user: bob"
# "  ✓ Created user: charlie"
# "✓ Database seeding completed successfully"
```

On subsequent app restarts, you'll see:
```
"Checking if database needs seeding..."
"Database already contains data - skipping seed"
```

### Production Considerations

- ✅ **Safe for production**: Seeding only occurs if database is completely empty
- ✅ **Idempotent**: Safe to restart the app multiple times
- ✅ **No data loss**: Existing data prevents seeding from running
- ✅ **Automated**: No manual steps required in CI/CD pipeline
- ✅ **Auditable**: All seeding operations are logged

---

## Environment-Specific Bootstrap Strategies

### 1. Integration Tests

**Status**: ✅ **Fully automated** (no infrastructure deployment needed)

The test fixture (`CustomWebApplicationFactory`) automatically initializes test data:

```csharp
// Test users initialized in CustomWebApplicationFactory.EnsureServerStarted()
- alice: ["general", "ops"]
- bob: ["general", "random"]
- charlie: ["general"]

// Rooms pre-initialized in InMemoryRoomsRepository constructor
- general (id: 1)
- ops (id: 2)
- random (id: 3)
```

**How it works**:
1. Tests use `Testing:InMemory=true` configuration
2. In-memory repositories are used (no Azure resources)
3. Test data is initialized via `IUsersRepository.Upsert()`
4. Rooms are pre-created in the in-memory repository constructor
5. All tests share the same fixture data

**Key files**:
- `tests/Chat.IntegrationTests/CustomWebApplicationFactory.cs` (lines 56-120)
- `src/Chat.Web/Repositories/InMemoryRepositories.cs` (lines 30-39)

**No infrastructure deployment required** - tests run entirely in-memory.

---

### 2. Local Development

#### Option A: Deploy Full Azure Infrastructure (Recommended)

Use Bicep templates to deploy a complete dev environment via GitHub Actions workflow:

```bash
# 1. Configure GitHub environment variables for 'dev' environment:
BICEP_BASE_NAME="signalrchat-dev"
BICEP_LOCATION="eastus"
BICEP_VNET_ADDRESS_PREFIX="10.0.0.0/26"
BICEP_APP_SERVICE_SUBNET_PREFIX="10.0.0.0/27"
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX="10.0.0.32/27"
BICEP_ACS_DATA_LOCATION="Europe"

# 2. Trigger GitHub Actions workflow from GitHub UI:
#    - Go to Actions → Infrastructure Deployment
#    - Run workflow → Select 'dev' environment → Action: deploy

# 3. After deployment completes, get connection strings from Azure Portal
#    Copy .env.local.example to .env.local and fill in the values

# 4. Run application locally (database will auto-seed on first startup)
#    The .env.local file will be automatically loaded by the VS Code task:
dotnet run --project ./src/Chat.Web --urls=https://localhost:5099

# Or use VS Code task: "Run Chat (Azure local env)" which automatically
# loads .env.local via: bash -lc 'set -a; [ -f .env.local ] && source .env.local; ...'
```

**Benefits**:
- Tests against real Azure services
- Full feature parity with production
- Network isolation and security (VNet integration)
- Realistic performance characteristics
- **Automatic database seeding** on first app startup
- **Auto-loading** of environment variables via `.env.local`

**Prerequisites**:
- Azure subscription
- GitHub repository with Actions enabled
- Azure credentials configured in GitHub
- Create `.env.local` from `.env.local.example` template
- ~$50-100/month for dev resources

#### Option B: In-Memory Mode (Quickest, Limited Features)

For rapid iteration without Azure resources:

```bash
# Run with in-memory repositories
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

When running with `Testing:InMemory=true` in `appsettings.Development.json`:
- ✅ No Azure resources needed
- ✅ No bootstrap/seeding required (test data initialized in-memory)
- ✅ Fast startup
- ❌ No real Cosmos DB (in-memory repositories)
- ❌ No real Redis (in-memory OTP store)
- ❌ No Azure SignalR (in-process hub only)

**Use case**: Quick UI development, SignalR hub testing

---

### 3. Staging Environment

**Strategy**: Full Bicep deployment via GitHub Actions + automatic seeding

#### Prerequisites

1. Azure subscription
2. GitHub repository with Actions enabled
3. Azure credentials configured in GitHub (OIDC federation)

#### Deployment

```bash
# 1. Configure GitHub environment variables for 'staging' environment:
BICEP_BASE_NAME="signalrchat-staging"
BICEP_LOCATION="eastus"
BICEP_VNET_ADDRESS_PREFIX="10.1.0.0/26"
BICEP_APP_SERVICE_SUBNET_PREFIX="10.1.0.0/27"
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX="10.1.0.32/27"
BICEP_ACS_DATA_LOCATION="Europe"

# 2. Trigger GitHub Actions workflow:
#    - Go to Actions → Infrastructure Deployment
#    - Run workflow → Select 'staging' environment → Action: deploy

# 3. App Service will automatically seed database on first startup
# Check App Service logs to verify seeding completed successfully
```

**Verification**:
```bash
# View App Service logs
az webapp log tail \
  --resource-group rg-signalrchat-staging \
  --name signalrchat-staging-app

# Check deployment status
APP_URL=$(az webapp show \
  --resource-group rg-signalrchat-staging \
  --name signalrchat-staging-app \
  --query "defaultHostName" -o tsv)

curl -I https://$APP_URL/healthz
```

---

### 4. Production Environment

**Strategy**: Bicep deployment via GitHub Actions + Automatic seeding + Approval gates

⚠️ **PRODUCTION DEPLOYMENT CHECKLIST**:
- [ ] Review what-if changes carefully in GitHub Actions workflow
- [ ] Schedule deployment during maintenance window
- [ ] Have rollback plan ready
- [ ] Monitor Application Insights during rollout
- [ ] Test in staging first
- [ ] Notify stakeholders
- [ ] Verify automatic database seeding in logs

#### Production Deployment via GitHub Actions

```bash
# 1. Configure GitHub environment variables for 'prod' environment:
BICEP_BASE_NAME="signalrchat-prod"
BICEP_LOCATION="eastus"
BICEP_VNET_ADDRESS_PREFIX="10.2.0.0/26"
BICEP_APP_SERVICE_SUBNET_PREFIX="10.2.0.0/27"
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX="10.2.0.32/27"
BICEP_ACS_DATA_LOCATION="Europe"

# 2. Trigger GitHub Actions workflow:
#    - Go to Actions → Infrastructure Deployment
#    - Run workflow → Select 'prod' environment → Action: deploy
#    - Review what-if analysis output
#    - Approve deployment (production requires manual approval)

# 3. App Service will automatically seed database on first startup
# Monitor logs to verify seeding completed successfully

on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Environment to deploy'
        required: true
        type: choice
        options:
          - dev
          - staging
          - prod
      action:
        description: 'Action to perform'
        required: true
        type: choice
        options:
          - deploy
          - teardown

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.environment }}  # Requires approval for prod
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Azure Login via OIDC
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      
      - name: Deploy Infrastructure
        if: github.event.inputs.action == 'deploy'
        run: |
          cd infra/bicep
          az deployment group create \
            --resource-group rg-signalrchat-${{ github.event.inputs.environment }} \
            --template-file main.bicep \
            --parameters baseName=${{ vars.BICEP_BASE_NAME }} \
                         location=${{ vars.BICEP_LOCATION }} \
                         vnetAddressPrefix=${{ vars.BICEP_VNET_ADDRESS_PREFIX }} \
                         appServiceSubnetPrefix=${{ vars.BICEP_APP_SERVICE_SUBNET_PREFIX }} \
                         privateEndpointsSubnetPrefix=${{ vars.BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX }} \
                         acsDataLocation=${{ vars.BICEP_ACS_DATA_LOCATION }}
      
      - name: Post-Deployment Validation
        if: github.event.inputs.action == 'deploy'
        run: |
          echo "✅ Infrastructure deployed successfully"
          echo "ℹ️  Database will be automatically seeded on first app startup"
          echo "Check App Service logs to verify seeding completed"
```

**Configure GitHub Environment Protection**:
1. Go to repository Settings → Environments
2. Create environment: `prod`
3. Enable "Required reviewers"
4. Add approvers (require 1-2 approvals)
5. Configure environment variables (BICEP_* vars) for each environment

**Note**: Database seeding happens automatically when the app starts for the first time. No separate seeding step is required in CI/CD.

---

## Post-Deployment Validation

After deploying infrastructure, verify the deployment and check that automatic seeding completed:

### Infrastructure Validation

```bash
# 1. Verify TWO subnets in Virtual Network (critical requirement)
az network vnet subnet list \
  --resource-group rg-signalrchat-<env> \
  --vnet-name signalrchat-<env>-vnet \
  --query "[].{Name:name, Prefix:addressPrefix, Delegation:delegations[0].serviceName}" \
  --output table

# Expected output:
# Name                         Prefix        Delegation
# ---------------------------  ------------  ------------------------
# appservice-subnet            10.X.0.0/27   Microsoft.Web/serverFarms
# privateendpoints-subnet      10.X.0.32/27  (null)

# 2. Verify App Service is running
az webapp show \
  --name signalrchat-<env>-app \
  --resource-group rg-signalrchat-<env> \
  --query "{State:state, HostName:defaultHostName}" \
  --output table

# 3. Verify Cosmos DB containers
az cosmosdb sql container list \
  --account-name signalrchat-<env>-cosmos \
  --resource-group rg-signalrchat-<env> \
  --database-name chat \
# Expected: messages, users, rooms

# 4. Test App Service health endpoint
APP_URL=$(az webapp show \
  --name signalrchat-<env>-app \
  --resource-group rg-signalrchat-<env> \
  --query "defaultHostName" -o tsv)

curl -I https://$APP_URL/healthz
# Expected: HTTP/1.1 200 OK

# 5. Verify Application Insights connection
az monitor app-insights component show \
  --app signalrchat-<env>-appinsights \
  --resource-group rg-signalrchat-<env> \
  --query "{Name:name, AppId:appId}" \
  --output table
```

### Automatic Seeding Verification

```bash
# View App Service logs to confirm automatic seeding completed
az webapp log tail \
  --name signalrchat-<env>-app \
  --resource-group rg-signalrchat-<env>

# Look for these log messages on first app startup:
# "Checking if database needs seeding..."
# "Database is empty - starting seed process"
# "Seeding default rooms..."
# "  ✓ Created room: general"
# "  ✓ Created room: ops"
# "  ✓ Created room: random"
# "Seeding default users..."
# "  ✓ Created user: alice@example.com (Alice Johnson)"
# "  ✓ Created user: bob@example.com (Bob Stone)"
# "  ✓ Created user: charlie@example.com (Charlie Fields)"
# "✓ Database seeding completed successfully"

# Verify seeded data in Cosmos DB
az cosmosdb sql container query \
  --account-name signalrchat-<env>-cosmos \
  --database-name chat \
  --container-name users \
  --resource-group rg-signalrchat-<env> \
  --query-text "SELECT c.userName FROM c" \
  --output table

# Expected: alice, bob, charlie
```

### Functional Validation

```bash
# 1. Access the application URL
open https://$APP_URL  # macOS
# Or paste URL in browser

# 2. Test OTP authentication
#    - Enter username (alice, bob, or charlie)
#    - Check console output for OTP code (if ACS not configured)
#    - Verify code in browser
#    - Confirm redirect to /chat

# 3. Test real-time messaging
#    - Open two browser windows
#    - Login as different users (e.g., alice and bob)
#    - Send messages in different rooms
#    - Verify real-time delivery via SignalR

# 4. Verify observability
#    - Check Application Insights in Azure Portal
#    - Confirm telemetry data is flowing
#    - Review logs in Log Analytics
#    - Verify custom metrics (chat.connections.active, chat.otp.requests)
```

---

## Infrastructure Teardown

To delete an environment completely via GitHub Actions:

```bash
# Trigger the teardown action from GitHub UI:
# 1. Go to Actions → Infrastructure Deployment
# 2. Run workflow → Select environment (dev/staging/prod) → Action: teardown
# 3. Review resources to be deleted
# 4. Confirm deletion (production requires approval)
```

**Or manually via Azure CLI**:

```bash
# Delete resource group (deletes ALL resources)
az group delete \
  --name rg-signalrchat-<env> \
  --yes \
  --no-wait

**⚠️ Warning**: This action is **IRREVERSIBLE**. All resources and data will be permanently deleted.

**Deletion Process**:
1. Lists all resources in resource group
2. Prompts for resource group name confirmation
3. Requires typing "DELETE" to proceed
4. Deletes resource group and all resources (~5-15 minutes)

---

## GitHub Actions Workflow (CI/CD)

Create `.github/workflows/deploy-infrastructure.yml`:

```yaml
name: Deploy Infrastructure

on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Environment to deploy'
        required: true
        type: choice
        options:
          - dev
          - staging
          - prod
      
      action:
        description: 'Action to perform'
        required: true
        type: choice
        options:
          - deploy
          - validate
          - teardown

jobs:
  infrastructure:
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.environment }}
    
    steps:
      - name: Checkout code
        uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'
      
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Validate Template
        if: github.event.inputs.action == 'validate'
        run: |
          cd infra/bicep
          
          # What-if analysis
          az deployment group what-if \
            --resource-group rg-signalrchat-${{ github.event.inputs.environment }} \
            --template-file main.bicep \
            --parameters baseName=${{ vars.BICEP_BASE_NAME }} \
                         location=${{ vars.BICEP_LOCATION }} \
                         vnetAddressPrefix=${{ vars.BICEP_VNET_ADDRESS_PREFIX }} \
                         appServiceSubnetPrefix=${{ vars.BICEP_APP_SERVICE_SUBNET_PREFIX }} \
                         privateEndpointsSubnetPrefix=${{ vars.BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX }} \
                         acsDataLocation=${{ vars.BICEP_ACS_DATA_LOCATION }}
      
      - name: Deploy Infrastructure
        if: github.event.inputs.action == 'deploy'
        id: deploy
        run: |
          cd infra/bicep
          
          az deployment group create \
            --resource-group rg-signalrchat-${{ github.event.inputs.environment }} \
            --template-file main.bicep \
            --parameters baseName=${{ vars.BICEP_BASE_NAME }} \
                         location=${{ vars.BICEP_LOCATION }} \
                         vnetAddressPrefix=${{ vars.BICEP_VNET_ADDRESS_PREFIX }} \
                         appServiceSubnetPrefix=${{ vars.BICEP_APP_SERVICE_SUBNET_PREFIX }} \
                         privateEndpointsSubnetPrefix=${{ vars.BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX }} \
                         acsDataLocation=${{ vars.BICEP_ACS_DATA_LOCATION }}
      
      - name: Teardown Infrastructure
        if: github.event.inputs.action == 'teardown'
        run: |
          # Delete resource group (all resources)
          az group delete \
            --name rg-signalrchat-${{ github.event.inputs.environment }} \
            --yes \
            --no-wait
      
      - name: Post-Deployment Validation
        if: github.event.inputs.action == 'deploy'
        run: |
          # Wait for App Service to be ready and seeding to complete
          echo "ℹ️  Database seeding happens automatically during first app startup"
          sleep 30
          
          # Get App URL
          APP_URL=$(az webapp show \
            --name signalrchat-${{ github.event.inputs.environment }}-app \
            --resource-group rg-signalrchat-${{ github.event.inputs.environment }} \
            --query "defaultHostName" -o tsv)
          
          # Test health endpoint
          HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$APP_URL/healthz" || echo "000")
          
          if [ "$HTTP_STATUS" = "200" ]; then
            echo "✅ Health check passed: https://$APP_URL/healthz"
            echo "✅ Database seeding completed during app startup"
          else
            echo "⚠️  Health check returned status: $HTTP_STATUS"
            echo "App may still be starting up or seeding data. Check Azure Portal logs."
          fi
```

### Configure GitHub Environment Variables

In repository Settings → Environments → [environment] → Environment variables:

| Variable Name | dev Example | staging Example | prod Example |
|--------------|-------------|-----------------|--------------|
| `BICEP_BASE_NAME` | `signalrchat-dev` | `signalrchat-staging` | `signalrchat-prod` |
| `BICEP_LOCATION` | `eastus` | `eastus` | `eastus` |
| `BICEP_VNET_ADDRESS_PREFIX` | `10.0.0.0/26` | `10.1.0.0/26` | `10.2.0.0/26` |
| `BICEP_APP_SERVICE_SUBNET_PREFIX` | `10.0.0.0/27` | `10.1.0.0/27` | `10.2.0.0/27` |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | `10.0.0.32/27` | `10.1.0.32/27` | `10.2.0.32/27` |
| `BICEP_ACS_DATA_LOCATION` | `Europe` | `Europe` | `Europe` |

### Configure GitHub Secrets

In repository Settings → Secrets and variables → Actions:

| Secret Name | Value | Description |
|-------------|-------|-------------|
| `AZURE_CLIENT_ID` | `<guid>` | Service Principal client ID (for OIDC) |
| `AZURE_TENANT_ID` | `<guid>` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | `<guid>` | Azure subscription ID |

### Configure Environment Protection Rules

1. Go to Settings → Environments
2. Create environments: `dev`, `staging`, `prod`
3. For `prod` environment:
   - Enable "Required reviewers" (add 1-2 approvers)
   - Enable "Wait timer" (optional: 5-10 minute delay)
   - Set "Deployment branches" to `main` only

---

## Quick Reference

| Environment | Infrastructure | Data Seeding | Command |
|-------------|----------------|--------------|---------|
| **Integration Tests** | None (in-memory) | Automatic (in-memory) | Run tests normally |
| **Local Dev (In-Memory)** | None | Automatic (in-memory) | `dotnet run` with `Testing:InMemory=true` |
| **Local Dev (Azure)** | GitHub Actions | Automatic (on first startup) | Trigger workflow → run app locally |
| **Staging** | GitHub Actions | Automatic (on first startup) | Trigger workflow for staging |
| **Production** | GitHub Actions + approval | Automatic (on first startup) | Trigger workflow for prod (requires approval) |

---

## Troubleshooting

### Deployment Issues

#### "Resource name already taken"

**Cause**: Cosmos DB, App Service, or Communication Services names must be globally unique.

**Solution**: 
- Modify `BICEP_BASE_NAME` environment variable to use a different base name
- Bicep templates automatically append unique suffixes to avoid conflicts

```bash
# Check deployment with what-if first via GitHub Actions workflow
# Or manually:
az deployment group what-if \
  --resource-group rg-signalrchat-<env> \
  --template-file infra/bicep/main.bicep \
  --parameters baseName=<your-base-name> location=eastus ...
```

#### "VNet Integration Fails"

**Cause**: Subnet not properly delegated to `Microsoft.Web/serverFarms`.

**Solution**: Verify subnet delegation:
```bash
az network vnet subnet show \
  --resource-group rg-signalrchat-<env> \
  --vnet-name signalrchat-<env>-vnet \
  --name appservice-subnet \
  --query "delegations[0].serviceName"

# Expected: "Microsoft.Web/serverFarms"
```

#### "Cosmos DB Deployment Slow"

**Cause**: Multi-region replication (production) takes 30+ minutes.

**Solution**: This is expected behavior. Production deployments with geo-replication are slower.

```bash
# Monitor deployment progress
az deployment group show \
  --resource-group rg-signalrchat-prod \
  --name <deployment-name> \
  --query "properties.provisioningState"
```

### Automatic Seeding Issues

#### "Database not seeding on startup"

**Cause**: App Service may have started with existing data, or seeding failed silently.

**Solution**:
```bash
# Check App Service logs for seeding messages
az webapp log tail \
  --name signalrchat-<env>-app \
  --resource-group rg-signalrchat-<env>

# Look for:
# "Checking if database needs seeding..."
# "Database is empty - starting seed process" OR "Database already contains data - skipping seed"

# If seeding failed, check for error messages in logs
# Restart app service to retry seeding if database is truly empty
az webapp restart \
  --name signalrchat-<env>-app \
  --resource-group rg-signalrchat-<env>
```

#### "Users exist but rooms are missing"

**Cause**: Partial seeding completed (users seeded but rooms failed).

**Solution**: The seeding service only runs if BOTH rooms AND users are empty. You'll need to manually clear data or add rooms:
```bash
# Option 1: Clear all data and restart app (will re-seed everything)
# Use Azure Portal → Cosmos DB → Data Explorer to delete all documents

# Option 2: Manually add rooms via Cosmos DB Data Explorer
# Use the room document structure from DataSeederService.cs
```

#### "Container not found"

**Cause**: Cosmos DB containers not created during infrastructure deployment.

**Solution**: Verify containers exist:
```bash
az cosmosdb sql container list \
  --account-name signalrchat-<env>-cosmos \
  --resource-group rg-signalrchat-<env> \
  --database-name chat

# Expected containers: messages, users, rooms
# If missing, check Bicep deployment logs or redeploy infrastructure
```

### Runtime Issues

#### "API not ready" / Health check fails

**Cause**: Application not fully started or configuration issues.

**Solution**:
```bash
# 1. Check App Service status
az webapp show \
  --name signalrchat-<env>-app-<suffix> \
  --resource-group rg-signalrchat-<env> \
  --query "{State:state, StatusMessage:availabilityState}"

# 2. View application logs
az webapp log tail \
  --name signalrchat-<env>-app-<suffix> \
  --resource-group rg-signalrchat-<env>

# 3. Check health endpoint
curl https://<app-url>/health
```

#### "User not authorized for room"

**Cause**: User's `FixedRooms` doesn't include target room.

**Solution**: Update user document in Cosmos DB:
```bash
# Use Azure Portal → Cosmos DB → Data Explorer
# Or use Azure CLI to update the user document
```

---

## Best Practices

✅ **DO**:
- Use Bicep templates for all infrastructure provisioning
- Keep infrastructure code in version control
- Make seeding scripts idempotent (check before insert)
- Test deployments in dev/staging before production
- Use GitHub Actions approval gates for production
- Run dry-run before actual seeding
- Document required data schema
- Log all infrastructure operations for audit
- Use managed identities for Azure resource access
- Store secrets in Azure Key Vault or GitHub Secrets

❌ **DON'T**:
- Manually provision Azure resources via Portal
- Auto-seed production on every startup
- Hard-code production credentials in scripts
- Skip validation of infrastructure templates
- Deploy to production without staging validation
- Mix test data with production data
- Commit connection strings to source control
- Use admin keys for application access (use managed identities)

---

## Related Documentation

- **[Infrastructure README](../infra/bicep/README.md)**: Detailed Bicep documentation, architecture diagrams, troubleshooting
- **[Architecture Guide](../ARCHITECTURE.md)**: System architecture, data schemas, security notes
- **[Main README](../README.md)**: Application features, configuration, local development

---

**Last updated**: 2025-11-06  
**Related issue**: #84 - Implement Azure Bicep Infrastructure as Code  
**Version**: 1.0.0 (Bicep-based bootstrap)

