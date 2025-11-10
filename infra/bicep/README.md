# Azure Infrastructure as Code (Bicep)

This directory contains the complete Infrastructure as Code (IaC) implementation for the SignalR Chat application using Azure Bicep templates.

## 📋 Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Resource Naming Convention](#resource-naming-convention)
- [Environments](#environments)
- [Getting Started](#getting-started)
- [Deployment](#deployment)
- [Validation](#validation)
- [Teardown](#teardown)
- [Troubleshooting](#troubleshooting)

## 🏗️ Overview

This IaC implementation enables:

- **Reproducible Infrastructure**: Deploy consistent environments across dev, staging, and production
- **Version Control**: Track infrastructure changes alongside application code
- **Automated Deployments**: CI/CD pipeline integration with GitHub Actions
- **Environment Isolation**: Separate VNets, connection strings, and resources per environment
- **Security Best Practices**: VNet integration, private endpoints, managed identities, TLS 1.2

## 🏛️ Architecture

### Azure Resources

The infrastructure deploys the following Azure resources:

| Resource | Purpose | Environments |
|----------|---------|--------------|
| **Virtual Network** | Network isolation with **2 subnets** (App Service integration + Private Endpoints) | dev, staging, prod |
| **Network Security Groups** | Security rules for subnets | dev, staging, prod |
| **Log Analytics Workspace** | Centralized logging and monitoring | dev, staging, prod |
| **Application Insights** | Application performance monitoring | dev, staging, prod |
| **Cosmos DB** | NoSQL database (messages, users, rooms) | dev, staging, prod |
| **Azure Cache for Redis** | Session storage and caching | dev, staging, prod |
| **Azure SignalR Service** | Real-time communication hub | dev, staging, prod |
| **Azure Communication Services** | Email and SMS capabilities | dev, staging, prod |
| **App Service Plan** | Web application hosting (Windows, .NET 9.0) | dev, staging, prod |
| **App Service (Web App)** | SignalR Chat application with VNet integration and outbound routing | dev, staging, prod |

### Network Architecture

Each environment has:

- **Virtual Network** with two dedicated subnets:
  1. **App Service Subnet**: Delegated to `Microsoft.Web/serverFarms` for VNet integration
  2. **Private Endpoints Subnet**: For secure Azure service connections

- **VNet Integration & Outbound Routing**:
  - App Service connected to VNet via App Service subnet
  - `outboundVnetRouting.allTraffic = true` configured (routes ALL outbound traffic through VNet)
  - Required for Windows App Services to use private endpoints
  - Custom DNS configured for private endpoint resolution (hub DNS forwarder)
  - All Azure service connections use private IP addresses (no public internet traffic)

- **Network Security Groups** (NSGs):
  - App Service NSG: Allows HTTPS (443), HTTP (80) inbound
  - Private Endpoints NSG: Restrictive rules for internal traffic

### Data Architecture

**Cosmos DB Containers**:
1. **messages** - Chat messages (partition key: `/roomId`)
2. **users** - User profiles (partition key: `/userName`, unique constraint on `/phoneNumber`)
3. **rooms** - Chat rooms (partition key: `/id`)

**Consistency Level**: Session (balance of consistency, performance, availability)

## ✅ Prerequisites

Before deploying, ensure you have:

1. **Azure CLI** (v2.50.0 or later)
   ```bash
   az --version
   ```

2. **Azure Subscription** with appropriate permissions:
   - Contributor role (minimum)
   - Owner role (recommended for RBAC assignments)

3. **Azure Login**
   ```bash
   az login
   az account set --subscription "<subscription-id>"
   ```

4. **Bicep CLI** (included with Azure CLI)
   ```bash
   az bicep version
   ```

5. **jq** (for parsing JSON output in scripts)
   ```bash
   brew install jq  # macOS
   ```

## 📁 Project Structure

```
infra/bicep/
├── main.bicep                          # Main orchestration template
├── main.parameters.dev.bicepparam      # Development environment parameters
├── main.parameters.staging.bicepparam  # Staging environment parameters
├── main.parameters.prod.bicepparam     # Production environment parameters
├── modules/
│   ├── networking.bicep                # VNet, subnets, NSGs
│   ├── monitoring.bicep                # Log Analytics, Application Insights
│   ├── cosmos-db.bicep                 # Cosmos DB account + containers
│   ├── redis.bicep                     # Azure Cache for Redis
│   ├── signalr.bicep                   # Azure SignalR Service
│   ├── communication.bicep             # Azure Communication Services
│   └── app-service.bicep               # App Service Plan + Web App
└── README.md                           # This file
```

## 🏷️ Resource Naming Convention

Resources follow the pattern: `{baseName}-{environment}-{resourceType}`

**Examples**:
- Virtual Network: `signalrchat-dev-vnet`
- App Service: `signalrchat-prod-app`
- Cosmos DB: `signalrchat-staging-cosmos`
- Redis Cache: `signalrchat-dev-redis`

**Global Resources** (must be globally unique):
- Cosmos DB: `{baseName}-{environment}-cosmos-{uniqueSuffix}`
- App Service: `{baseName}-{environment}-app-{uniqueSuffix}`
- Communication Services: `{baseName}-{environment}-acs-{uniqueSuffix}`

## 🌍 Environments

### Development (dev)

**Purpose**: Local development and feature testing

| Resource | SKU/Tier | Configuration |
|----------|----------|---------------|
| VNet | Standard | 10.50.8.0/26 |
| App Service Plan | P0V4 PremiumV4 Windows | 1 instance, .NET 9.0 |
| Cosmos DB | Autoscale (400 RU/s max) | Single region, zone-redundant |
| Redis Cache | Balanced_B1 (Azure Managed Redis) | 2 GB |
| SignalR Service | Standard_S1 | 1 unit |
| Log Analytics | 30-day retention | 1 GB daily cap |

**Cost Estimate**: ~$150-250/month

### Staging (staging)

**Purpose**: Pre-production validation and integration testing

| Resource | SKU/Tier | Configuration |
|----------|----------|---------------|
| VNet | Standard | 10.50.8.64/26 |
| App Service Plan | P0V4 PremiumV4 Windows | 2 instances (AZ), .NET 9.0 |
| Cosmos DB | Autoscale (1000 RU/s max) | Single region, zone-redundant |
| Redis Cache | Balanced_B3 (Azure Managed Redis) | 6 GB |
| SignalR Service | Standard_S1 | 1 unit |
| Log Analytics | 90-day retention | 5 GB daily cap |

**Cost Estimate**: ~$350-500/month

### Production (prod)

**Purpose**: Live production environment

| Resource | SKU/Tier | Configuration |
|----------|----------|---------------|
| VNet | Standard | 10.50.8.128/26 |
| App Service Plan | P0V4 PremiumV4 Windows | 3 instances with AZ, .NET 9.0 |
| Cosmos DB | Autoscale (4000 RU/s max) | Multi-region (polandcentral + germanywestcentral), zone-redundant |
| Redis Cache | Balanced_B5 (Azure Managed Redis) | 12 GB |
| SignalR Service | Standard_S1 | 5 units |
| Log Analytics | 365-day retention | 10 GB daily cap |

**Cost Estimate**: ~$1200-1800/month

## 🚀 Getting Started

### 1. Configure GitHub Environments

Create three GitHub environments in your repository (Settings → Environments):

- **dev** (Development)
- **staging** (Staging)
- **prod** (Production - with protection rules)

### 2. Configure GitHub Secrets

For each environment, add the following secrets:

**Required Secrets**:
- `AZURE_CLIENT_ID` - Service Principal Application ID
- `AZURE_TENANT_ID` - Azure AD Tenant ID
- `AZURE_SUBSCRIPTION_ID` - Azure Subscription ID

**Required Variables** (configure for each environment):
- `BICEP_BASE_NAME` - Base name for resources (e.g., `signalrchat`)
- `BICEP_LOCATION` - Azure region (e.g., `eastus`)
- `BICEP_VNET_ADDRESS_PREFIX` - VNet CIDR block (e.g., `10.0.0.0/16` for dev, `10.1.0.0/16` for staging, `10.2.0.0/16` for prod)
- `BICEP_APP_SERVICE_SUBNET_PREFIX` - App Service subnet CIDR (e.g., `10.0.1.0/24` for dev, `10.1.1.0/24` for staging, `10.2.1.0/24` for prod)
- `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` - Private Endpoints subnet CIDR (e.g., `10.0.2.0/24` for dev, `10.1.2.0/24` for staging, `10.2.2.0/24` for prod)
- `BICEP_ACS_DATA_LOCATION` - Azure Communication Services data location (e.g., `United States`)

### 3. Set Up Azure Service Principal

Create a service principal with federated credentials for GitHub Actions:

```bash
# Create service principal
az ad sp create-for-rbac \
  --name "github-actions-signalrchat" \
  --role contributor \
  --scopes /subscriptions/{subscription-id} \
  --sdk-auth

# Configure federated credential for GitHub Actions
az ad app federated-credential create \
  --id {app-id} \
  --parameters '{
    "name": "github-actions-deploy",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:{org}/{repo}:environment:prod",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

### 4. Review Parameters

Parameter files (`*.bicepparam`) are for **reference only** and contain sample values. They document which parameters are required but are **NOT used** by GitHub Actions.

**GitHub Actions deployment uses only environment variables:**

```yaml
# GitHub Actions passes parameters directly from environment variables:
az deployment group create \
  --template-file main.bicep \
  --parameters baseName=$BICEP_BASE_NAME \
  --parameters environment=$ENVIRONMENT \
  --parameters location=$BICEP_LOCATION \
  --parameters vnetAddressPrefix=$BICEP_VNET_ADDRESS_PREFIX \
  --parameters appServiceSubnetPrefix=$BICEP_APP_SERVICE_SUBNET_PREFIX \
  --parameters privateEndpointsSubnetPrefix=$BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX \
  --parameters acsDataLocation="$BICEP_ACS_DATA_LOCATION"
```

**Parameter files are useful for**:
- Documentation: See what parameters exist and their sample values
- Local testing: Use with `az deployment group create --parameters main.parameters.dev.bicepparam`
- Reference: Understand expected values for different environments

## 📦 Deployment

All deployments are executed through GitHub Actions workflows. Manual deployments from terminal are not supported.

### Validate Infrastructure

1. Go to **Actions** tab in GitHub
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Configure:
   - **Environment**: `dev`, `staging`, or `prod`
   - **Action**: `validate`
5. Click **Run workflow**

The workflow will:
- ✅ Check Bicep syntax
- ✅ Validate parameters
- ✅ Run what-if analysis (preview changes)

### Deploy to Development

1. Go to **Actions** tab in GitHub
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Configure:
   - **Environment**: `dev`
   - **Action**: `deploy`
5. Click **Run workflow**

**Steps**:
1. Validates Bicep template
2. Creates resource group
3. Runs what-if preview
4. Deploys all resources
5. Seeds database
6. Validates deployment (health checks, subnet count)

**Deployment Time**: ~20-30 minutes

### Deploy to Staging

Same process as development, select `staging` environment.

**Deployment Time**: ~25-35 minutes

### Deploy to Production

Same process, select `prod` environment.

**⚠️ Production Deployment**:
- Requires approval (configure in GitHub environment protection rules)
- Review what-if changes carefully in workflow logs
- Consider deploying during maintenance windows
- Monitor Application Insights during rollout

**Deployment Time**: ~30-45 minutes (includes geo-replication)

## ✅ Validation

### Automated Validation

The GitHub Actions workflow automatically validates:

1. **Bicep Syntax**: Compiles `main.bicep` and all modules
2. **Template Validation**: Checks against Azure Resource Manager
3. **What-If Analysis**: Shows changes before deployment
4. **Post-Deployment Checks**:
   - Health endpoint returns 200 OK
   - Virtual Network has exactly **2 subnets**
   - All resources deployed successfully

### Manual Validation

After deployment, verify in Azure Portal or via Azure CLI:

1. **App Service is running**:
   ```bash
   az webapp show --name signalrchat-dev-app-<suffix> \
     --resource-group rg-signalrchat-dev \
     --query "state"
   ```

2. **Virtual Network has 2 subnets**:
   ```bash
   az network vnet subnet list \
     --resource-group rg-signalrchat-dev \
     --vnet-name signalrchat-dev-vnet \
     --query "[].{Name:name, Prefix:addressPrefix}" \
     --output table
   ```

3. **Cosmos DB containers exist**:
   ```bash
   az cosmosdb sql container list \
     --account-name signalrchat-dev-cosmos-<suffix> \
     --resource-group rg-signalrchat-dev \
     --database-name ChatDatabase \
     --query "[].name" \
     --output table
   ```

4. **App Service has VNet integration**:
   ```bash
   az webapp vnet-integration list \
     --name signalrchat-dev-app-<suffix> \
     --resource-group rg-signalrchat-dev
   ```

5. **Application Insights connected**:
   ```bash
   az monitor app-insights component show \
     --app signalrchat-dev-appinsights \
     --resource-group rg-signalrchat-dev \
     --query "connectionString"
   ```

## 🗑️ Teardown

### Delete Environment via GitHub Actions

1. Go to **Actions** tab in GitHub
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Configure:
   - **Environment**: `dev`, `staging`, or `prod`
   - **Action**: `teardown`
5. Click **Run workflow**

**⚠️ Warning**: This action is **IRREVERSIBLE**. All resources in the resource group will be permanently deleted.

**Deletion Time**: ~5-15 minutes

### Manual Teardown (Emergency Only)

If GitHub Actions is unavailable, you can manually delete via Azure CLI:

```bash
# List resources before deletion
az resource list --resource-group rg-signalrchat-dev --output table

# Delete resource group (requires confirmation)
az group delete --name rg-signalrchat-dev --yes
```

## 🔧 Troubleshooting

### Common Issues

#### 1. GitHub Actions Workflow Fails - Authentication Error

**Cause**: Missing or incorrect Azure credentials in GitHub secrets.

**Solution**: 
- Verify `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` are set in GitHub environment
- Ensure federated credentials are configured for GitHub Actions
- Check service principal has Contributor role

#### 2. Deployment Fails with "Resource name already taken"

**Cause**: Cosmos DB, App Service, or Communication Services names must be globally unique.

**Solution**: Modify `BICEP_BASE_NAME` variable in GitHub environment or let Bicep generate unique suffix:
```bicep
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 6)
```

#### 3. VNet Integration Fails

**Cause**: Subnet not properly delegated to `Microsoft.Web/serverFarms`.

**Solution**: Verify subnet delegation in `networking.bicep`:
```bicep
delegations: [
  {
    name: 'delegation'
    properties: {
      serviceName: 'Microsoft.Web/serverFarms'
    }
  }
]
```

#### 4. Cosmos DB Deployment Slow

**Cause**: Multi-region replication (production) takes longer.

**Solution**: This is expected. Production deployments with geo-replication can take 30+ minutes. Monitor workflow progress in GitHub Actions.

#### 5. Database Seeding Fails

**Cause**: Cosmos DB connection string not properly retrieved or database not ready.

**Solution**: 
- Check workflow logs for connection string retrieval
- Cosmos DB may need a few minutes to be fully ready after provisioning
- Retry the workflow run

#### 6. What-If Shows Unexpected Changes

**Cause**: Azure API versions changed or resources were manually modified.

**Solution**: Review changes in workflow logs. If legitimate, proceed. If unexpected:
- Check for manual changes in Azure Portal
- Verify parameter files match intended configuration
- Consider redeploying from known good state

### Debugging in GitHub Actions

**View Workflow Logs**:
1. Go to **Actions** tab
2. Click on the failed workflow run
3. Expand each step to see detailed logs

**Re-run Failed Jobs**:
1. Open the failed workflow run
2. Click **Re-run jobs** → **Re-run failed jobs**

**Check Deployment Outputs**:
Deployment outputs are shown in the **Deployment Summary** section of the workflow run.

### Manual Debugging Commands

If you need to debug manually via Azure CLI:

**List all resources in resource group**:
```bash
az resource list --resource-group rg-signalrchat-dev --output table
```

**Check deployment status**:
```bash
az deployment group list \
  --resource-group rg-signalrchat-dev \
  --query "[0].{Name:name, State:properties.provisioningState, Timestamp:properties.timestamp}" \
  --output table
```

**View deployment logs**:
```bash
az deployment group show \
  --resource-group rg-signalrchat-dev \
  --name <deployment-name> \
  --query "properties.error"
```

**Test App Service endpoint**:
```bash
APP_URL=$(az deployment group show \
  --resource-group rg-signalrchat-dev \
  --name <deployment-name> \
  --query "properties.outputs.appUrl.value" -o tsv)

curl -I $APP_URL
```

### Support

For additional help:
1. Review workflow logs in GitHub Actions
2. Check [ARCHITECTURE.md](../../ARCHITECTURE.md) for system design
3. Review Azure deployment logs in Azure Portal
4. Check Application Insights for runtime errors
5. Consult [Azure Bicep documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)

## 📚 Additional Resources

- [GitHub Actions for Azure](https://learn.microsoft.com/azure/developer/github/github-actions)
- [Azure Bicep Best Practices](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices)
- [Azure Well-Architected Framework](https://learn.microsoft.com/azure/well-architected/)
- [Cosmos DB Best Practices](https://learn.microsoft.com/azure/cosmos-db/nosql/best-practice-dotnet)
- [App Service Best Practices](https://learn.microsoft.com/azure/app-service/app-service-best-practices)
- [Azure SignalR Service Documentation](https://learn.microsoft.com/azure/azure-signalr/)

---

**Related Issue**: #84 - Implement Azure Bicep Infrastructure as Code  
**Deployment Method**: GitHub Actions (CI/CD Only)  
**Version**: 1.0.0  
**Last Updated**: 2025-11-06
