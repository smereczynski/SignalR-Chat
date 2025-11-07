# Bootstrap Guide

This document explains how to deploy and initialize the SignalR Chat application infrastructure and data from scratch using Azure Bicep templates.

## Overview

The Chat application deployment consists of two phases:

1. **Infrastructure Provisioning** (Azure Bicep): Deploy all Azure resources (VNet, Cosmos DB, Redis, SignalR, App Service, etc.)
2. **Data Seeding** (Chat.DataSeed tool): Initialize rooms and users in the provisioned database

This on-demand approach provides:
- ✅ **Reproducible infrastructure**: Deploy consistent environments across dev/staging/prod
- ✅ **Version control**: Track infrastructure changes alongside application code
- ✅ **Explicit control**: Infrastructure and data provisioning happen when YOU decide
- ✅ **No accidental overwrites**: Production data is never auto-seeded
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

For each environment (dev, staging, prod), configure these 6 required variables in GitHub:

**GitHub Repository → Settings → Secrets and variables → Actions → Variables**

| Variable Name | Description | Example (dev) | Example (staging) | Example (prod) |
|--------------|-------------|---------------|-------------------|----------------|
| `BICEP_BASE_NAME` | Base name for all resources | `signalrchat` | `signalrchat` | `signalrchat` |
| `BICEP_LOCATION` | Azure region | `polandcentral` | `polandcentral` | `polandcentral` |
| `BICEP_VNET_ADDRESS_PREFIX` | VNet CIDR block (/26) | `10.0.0.0/26` | `10.1.0.0/26` | `10.2.0.0/26` |
| `BICEP_APP_SERVICE_SUBNET_PREFIX` | First subnet (/27) | `10.0.0.0/27` | `10.1.0.0/27` | `10.2.0.0/27` |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | Second subnet (/27) | `10.0.0.32/27` | `10.1.0.32/27` | `10.2.0.32/27` |
| `BICEP_ACS_DATA_LOCATION` | ACS data location | `Europe` | `Europe` | `Europe` |

**⚠️ Critical**: Each environment MUST have a unique VNet address space to avoid conflicts.

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

## Phase 2: Data Seeding (Chat.DataSeed)

After infrastructure is deployed, initialize the database with rooms and users using the standalone seeding tool.

### Data Seeding Tool

**Location**: `tools/Chat.DataSeed/`

**Features**:
- Works like database migrations - runs on demand, not part of runtime app
- Reuses Chat.Web repositories and models
- Supports Cosmos DB (extensible to other backends)
- Command-line flags: `--clear`, `--dry-run`, `--environment`

### What It Seeds

**Rooms**:
- `general` (id: 1)
- `ops` (id: 2)
- `random` (id: 3)

**Users**:
- `alice`: FixedRooms=["general", "ops"], DefaultRoom="general"
- `bob`: FixedRooms=["general", "random"], DefaultRoom="general"
- `charlie`: FixedRooms=["general"], DefaultRoom="general"

### Seeding Development Environment

```bash
cd /Users/michal/Developer/SignalR-Chat

# Option 1: Use connection string from deployment output
export Cosmos__ConnectionString="<cosmos-connection-string-from-deployment>"

# Option 2: Fetch connection string from Azure
export Cosmos__ConnectionString=$(az cosmosdb keys list \
  --resource-group rg-signalrchat-dev \
  --name signalrchat-dev-cosmos-<suffix> \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" -o tsv)

# Seed the database
dotnet run --project tools/Chat.DataSeed -- --environment Development

# Or preview what would be created (dry run)
dotnet run --project tools/Chat.DataSeed -- --environment Development --dry-run
```

**Expected Output**:
```
=== Chat Data Seed Tool ===

Using Cosmos DB repositories (Database: ChatDatabase)
Starting data seed process...
Seeding rooms...
Room 'general' already exists (ID: 1) - skipping
Room 'ops' already exists (ID: 2) - skipping
Room 'random' already exists (ID: 3) - skipping
Seeding users...
Creating user: alice (Alice Johnson)
  ✓ User 'alice' created successfully
Creating user: bob (Bob Stone)
  ✓ User 'bob' created successfully
Creating user: charlie (Charlie Fields)
  ✓ User 'charlie' created successfully
Data seed process completed

✓ Data seeding completed successfully!
```

### Seeding Staging/Production

**Security Note**: Production seeding should be run from CI/CD pipeline or Azure Container Instance with proper credential management.

```bash
# For staging
export Cosmos__ConnectionString="<staging-cosmos-connection-string>"
dotnet run --project tools/Chat.DataSeed -- --environment Staging

# For production (use with caution)
export Cosmos__ConnectionString="<prod-cosmos-connection-string>"
dotnet run --project tools/Chat.DataSeed -- --environment Production --dry-run  # Preview first!
dotnet run --project tools/Chat.DataSeed -- --environment Production  # After review
```

### CI/CD Integration

Add seeding as a pipeline step after infrastructure deployment:

**GitHub Actions Example**:
```yaml
- name: Seed Database
  run: |
    export Cosmos__ConnectionString="${{ secrets.COSMOS_CONNECTION_STRING }}"
    dotnet run --project tools/Chat.DataSeed -- --environment Production
```

**Azure DevOps Example**:
```yaml
- task: DotNetCoreCLI@2
  displayName: 'Seed Production Data'
  inputs:
    command: 'run'
    projects: 'tools/Chat.DataSeed/Chat.DataSeed.csproj'
    arguments: '-- --environment Production'
  env:
    Cosmos__ConnectionString: $(CosmosConnectionString)
```

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

Use Bicep templates to deploy a complete dev environment:

```bash
# 1. Deploy infrastructure
cd infra/bicep
./scripts/deploy.sh dev rg-signalrchat-dev eastus

# 2. Configure local app to use deployed resources
# Create .env.local file in project root:
cat > .env.local << EOF
Cosmos__ConnectionString="<from-deployment-output>"
Redis__ConnectionString="<from-deployment-output>"
Azure__SignalR__ConnectionString="<from-deployment-output>"
Acs__ConnectionString="<from-deployment-output>"
EOF

# 3. Seed database
export Cosmos__ConnectionString="<from-deployment-output>"
dotnet run --project tools/Chat.DataSeed -- --environment Development

# 4. Run application
bash -lc 'set -a; [ -f .env.local ] && source .env.local; export ASPNETCORE_ENVIRONMENT=Development; dotnet run --project ./src/Chat.Web --urls=https://localhost:5099'
```

**Benefits**:
- Tests against real Azure services
- Full feature parity with production
- Network isolation and security (VNet integration)
- Realistic performance characteristics

**Prerequisites**:
- Azure subscription
- Azure CLI configured
- ~$50-100/month for dev resources

#### Option B: In-Memory Mode (Quickest, Limited Features)

For rapid iteration without Azure resources:

```bash
# Run with in-memory repositories
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

When running with `Testing:InMemory=true` in `appsettings.Development.json`:
- ✅ No Azure resources needed
- ✅ No bootstrap/seeding required
- ✅ Fast startup
- ❌ No real Cosmos DB (in-memory repositories)
- ❌ No real Redis (in-memory OTP store)
- ❌ No Azure SignalR (in-process hub only)

**Use case**: Quick UI development, SignalR hub testing

---

### 3. Staging Environment

**Strategy**: Full Bicep deployment + CI/CD integration

#### Prerequisites

1. Azure subscription
2. GitHub repository with Actions enabled (or Azure DevOps)
3. Service Principal with Contributor role

#### Manual Deployment

```bash
# 1. Deploy infrastructure
cd infra/bicep
./scripts/deploy.sh staging rg-signalrchat-staging eastus

# 2. Seed database (after deployment completes)
export Cosmos__ConnectionString=$(az cosmosdb keys list \
  --resource-group rg-signalrchat-staging \
  --name signalrchat-staging-cosmos-<suffix> \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" -o tsv)

dotnet run --project tools/Chat.DataSeed -- --environment Staging

# 3. Verify deployment
APP_URL=$(az deployment group show \
  --resource-group rg-signalrchat-staging \
  --name <deployment-name> \
  --query "properties.outputs.appUrl.value" -o tsv)

curl -I $APP_URL
```

#### Automated Deployment (GitHub Actions)

See section "GitHub Actions Workflow" below for CI/CD automation.

---

### 4. Production Environment

**Strategy**: Bicep deployment + Automated seeding via CI/CD + Approval gates

⚠️ **PRODUCTION DEPLOYMENT CHECKLIST**:
- [ ] Review what-if changes carefully
- [ ] Schedule deployment during maintenance window
- [ ] Have rollback plan ready
- [ ] Monitor Application Insights during rollout
- [ ] Test in staging first
- [ ] Notify stakeholders
- [ ] Backup critical data (if applicable)

#### Manual Production Deployment

```bash
# 1. Deploy infrastructure
cd infra/bicep
./scripts/deploy.sh prod rg-signalrchat-prod eastus

# Expected prompts:
# - What-if analysis (review changes)
# - Confirmation: "Do you want to proceed? (yes/no)"

# 2. Seed database (after deployment completes)
export Cosmos__ConnectionString=$(az cosmosdb keys list \
  --resource-group rg-signalrchat-prod \
  --name signalrchat-prod-cosmos-<suffix> \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" -o tsv)

# DRY RUN FIRST (highly recommended)
dotnet run --project tools/Chat.DataSeed -- --environment Production --dry-run

# Review dry-run output, then seed
dotnet run --project tools/Chat.DataSeed -- --environment Production

# 3. Post-deployment validation
# See "Post-Deployment Validation" section below
```

#### Automated Production Deployment

**GitHub Actions with Approval Gates** (recommended):

```yaml
# .github/workflows/deploy-infrastructure.yml
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

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.environment }}  # Requires approval for prod
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Deploy Infrastructure
        run: |
          cd infra/bicep
          ./scripts/deploy.sh ${{ github.event.inputs.environment }} \
            rg-signalrchat-${{ github.event.inputs.environment }} \
            eastus
      
      - name: Seed Database
        run: |
          export Cosmos__ConnectionString="${{ secrets.COSMOS_CONNECTION_STRING }}"
          dotnet run --project tools/Chat.DataSeed -- --environment Production
```

**Configure GitHub Environment Protection**:
1. Go to repository Settings → Environments
2. Create environment: `prod`
3. Enable "Required reviewers"
4. Add approvers (require 1-2 approvals)

---

## Post-Deployment Validation

After deploying infrastructure and seeding data, verify the deployment:

### Infrastructure Validation

```bash
# 1. Verify TWO subnets in Virtual Network (critical requirement)
az network vnet subnet list \
  --resource-group rg-signalrchat-<env> \
  --vnet-name signalrchat-<env>-vnet \
  --query "[].{Name:name, Prefix:addressPrefix, Delegation:delegations[0].serviceName}" \
  --output table

# Expected output:
# Name                      Prefix        Delegation
# ------------------------  ------------  ------------------------
# appservice-subnet         10.X.1.0/24   Microsoft.Web/serverFarms
# privateendpoints-subnet   10.X.2.0/24   (null)

# 2. Verify App Service is running
az webapp show \
  --name signalrchat-<env>-app-<suffix> \
  --resource-group rg-signalrchat-<env> \
  --query "{State:state, HostName:defaultHostName, VNetIntegration:siteConfig.vnetName}" \
  --output table

# 3. Verify Cosmos DB containers
az cosmosdb sql container list \
  --account-name signalrchat-<env>-cosmos-<suffix> \
  --resource-group rg-signalrchat-<env> \
  --database-name ChatDatabase \
  --query "[].name" \
  --output table

# Expected: messages, users, rooms

# 4. Test App Service endpoint
APP_URL=$(az deployment group show \
  --resource-group rg-signalrchat-<env> \
  --name <deployment-name> \
  --query "properties.outputs.appUrl.value" -o tsv)

curl -I $APP_URL/health
# Expected: HTTP/1.1 200 OK

# 5. Verify Application Insights connection
az monitor app-insights component show \
  --app signalrchat-<env>-appinsights \
  --resource-group rg-signalrchat-<env> \
  --query "{Name:name, AppId:appId, ConnectionString:connectionString}" \
  --output table
```

### Data Validation

```bash
# Verify seeded data using Azure Cosmos DB Data Explorer
# Or query via Azure CLI (requires cosmos-db extension)

# List users
az cosmosdb sql container item list \
  --account-name signalrchat-<env>-cosmos-<suffix> \
  --database-name ChatDatabase \
  --container-name users \
  --resource-group rg-signalrchat-<env> \
  --query "[].{UserName:userName, FixedRooms:fixedRooms}" \
  --output table

# Expected: alice, bob, charlie
```

### Functional Validation

```bash
# 1. Access the application URL
open $APP_URL  # macOS
# Or paste URL in browser

# 2. Test OTP authentication
#    - Enter username (alice, bob, or charlie)
#    - Check console output for OTP code (if ACS not configured)
#    - Verify code in browser
#    - Confirm redirect to /chat

# 3. Test real-time messaging
#    - Open two browser windows
#    - Login as different users
#    - Send messages
#    - Verify real-time delivery

# 4. Verify observability
#    - Check Application Insights in Azure Portal
#    - Confirm telemetry data is flowing
#    - Review logs in Log Analytics
```

---

## Infrastructure Teardown

To delete an environment completely:

```bash
cd infra/bicep

# Interactive deletion (requires confirmation)
./scripts/teardown.sh rg-signalrchat-dev

# Force deletion (no prompts - use with caution)
./scripts/teardown.sh rg-signalrchat-dev --force
```

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
          chmod +x scripts/validate.sh
          ./scripts/validate.sh ${{ github.event.inputs.environment }}
      
      - name: Deploy Infrastructure
        if: github.event.inputs.action == 'deploy'
        id: deploy
        run: |
          cd infra/bicep
          chmod +x scripts/deploy.sh
          
          # Non-interactive deployment for CI/CD
          export CONFIRM="yes"
          
          ./scripts/deploy.sh ${{ github.event.inputs.environment }} \
            rg-signalrchat-${{ github.event.inputs.environment }} \
            eastus
      
      - name: Seed Database
        if: github.event.inputs.action == 'deploy'
        env:
          COSMOS_CONNECTION_STRING: ${{ secrets[format('COSMOS_CONNECTION_STRING_{0}', github.event.inputs.environment)] }}
        run: |
          export Cosmos__ConnectionString="${COSMOS_CONNECTION_STRING}"
          
          # Dry run first
          dotnet run --project tools/Chat.DataSeed -- \
            --environment ${{ github.event.inputs.environment }} \
            --dry-run
          
          # Actual seeding
          dotnet run --project tools/Chat.DataSeed -- \
            --environment ${{ github.event.inputs.environment }}
      
      - name: Teardown Infrastructure
        if: github.event.inputs.action == 'teardown'
        run: |
          cd infra/bicep
          chmod +x scripts/teardown.sh
          
          # Force mode for automated teardown
          ./scripts/teardown.sh rg-signalrchat-${{ github.event.inputs.environment }} --force
      
      - name: Post-Deployment Validation
        if: github.event.inputs.action == 'deploy'
        run: |
          # Wait for App Service to be fully ready
          sleep 30
          
          # Get App URL from deployment output
          APP_URL=$(az deployment group show \
            --resource-group rg-signalrchat-${{ github.event.inputs.environment }} \
            --name signalrchat-${{ github.event.inputs.environment }}-$(date +%Y%m%d) \
            --query "properties.outputs.appUrl.value" -o tsv)
          
          # Test health endpoint
          curl -f $APP_URL/health || exit 1
          
          echo "✓ Deployment successful: $APP_URL"
```

### Configure GitHub Secrets

In repository Settings → Secrets and variables → Actions, add:

| Secret Name | Value | Description |
|-------------|-------|-------------|
| `AZURE_CREDENTIALS` | `{"clientId":"...","clientSecret":"...","subscriptionId":"...","tenantId":"..."}` | Service Principal credentials |
| `COSMOS_CONNECTION_STRING_dev` | `AccountEndpoint=https://...` | Dev Cosmos connection string |
| `COSMOS_CONNECTION_STRING_staging` | `AccountEndpoint=https://...` | Staging Cosmos connection string |
| `COSMOS_CONNECTION_STRING_prod` | `AccountEndpoint=https://...` | Production Cosmos connection string |

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
| **Integration Tests** | None (in-memory) | Automatic | Run tests normally |
| **Local Dev (In-Memory)** | None | Automatic | `dotnet run` with `Testing:InMemory=true` |
| **Local Dev (Azure)** | Bicep deployment | Manual | `./scripts/deploy.sh dev ...` + `dotnet run --project tools/Chat.DataSeed` |
| **Staging** | Bicep deployment | Manual/CI/CD | `./scripts/deploy.sh staging ...` + seeding |
| **Production** | Bicep deployment | CI/CD with approval | GitHub Actions workflow |

---

## Troubleshooting

### Deployment Issues

#### "Resource name already taken"

**Cause**: Cosmos DB, App Service, or Communication Services names must be globally unique.

**Solution**: 
- Modify `baseName` parameter in `.bicepparam` file
- Or let Bicep generate unique suffix automatically (already implemented)

```bash
# Check deployment with what-if first
cd infra/bicep
./scripts/validate.sh <environment>
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

### Seeding Issues

#### "Database connection not configured"

**Cause**: Cosmos connection string not set or invalid.

**Solution**:
```bash
# Fetch connection string from Azure
export Cosmos__ConnectionString=$(az cosmosdb keys list \
  --resource-group rg-signalrchat-<env> \
  --name signalrchat-<env>-cosmos-<suffix> \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" -o tsv)

# Verify it's set
echo $Cosmos__ConnectionString
```

#### "User/Room already exists - skipping"

**Cause**: Data already seeded (idempotent behavior).

**Solution**: This is expected. The seeding tool skips existing data.

To re-seed from scratch:
```bash
# Clear existing data first (with confirmation)
dotnet run --project tools/Chat.DataSeed -- --clear --environment Development
```

#### "Container not found"

**Cause**: Cosmos DB containers not created during infrastructure deployment.

**Solution**: Verify containers exist:
```bash
az cosmosdb sql container list \
  --account-name signalrchat-<env>-cosmos-<suffix> \
  --resource-group rg-signalrchat-<env> \
  --database-name ChatDatabase

# If missing, check Bicep deployment logs
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

