# Azure Infrastructure as Code (Bicep)

**Status**: Complete (Migrated from `infra/bicep/README.md`)
**Location**: `infra/bicep/`

This document describes the Bicep Infrastructure as Code (IaC) templates for deploying SignalR Chat to Azure.

---

## 📋 Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Resource Naming Convention](#resource-naming-convention)
- [Environments](#environments)
- [Deployment](#deployment)
- [Validation](#validation)
- [Troubleshooting](#troubleshooting)

---

## 🏗️ Overview

The Bicep templates in `infra/bicep/` enable:

- **Reproducible Infrastructure**: Deploy consistent environments across dev, staging, and production
- **Version Control**: Track infrastructure changes alongside application code
- **Automated Deployments**: CI/CD pipeline integration with GitHub Actions
- **Environment Isolation**: Separate VNets, connection strings, and resources per environment
- **Security Best Practices**: VNet integration, private endpoints, managed identities, TLS 1.2

**Deployment Method**: All deployments are executed through GitHub Actions workflows. See [GitHub Actions CI/CD](../github-actions.md) for details.

---

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
| **Azure Managed Redis** | Session storage and caching | dev, staging, prod |
| **Azure SignalR Service** | Real-time communication hub | dev, staging, prod |
| **Azure Communication Services** | Email and SMS capabilities | dev, staging, prod |
| **App Service Plan** | Web application hosting (Linux, .NET 9.0) | dev, staging, prod |
| **App Service (Web App)** | SignalR Chat application with VNet integration and outbound routing | dev, staging, prod |

### Network Architecture

Each environment has:

- **Virtual Network** with two dedicated subnets:
  1. **App Service Subnet**: Delegated to `Microsoft.Web/serverFarms` for VNet integration
  2. **Private Endpoints Subnet**: For secure Azure service connections

- **VNet Integration & Outbound Routing**:
  - App Service connected to VNet via App Service subnet
  - `outboundVnetRouting.allTraffic = true` configured (routes ALL outbound traffic through VNet)
  - Required for App Services to use private endpoints
  - Custom DNS configured for private endpoint resolution (hub DNS forwarder)
  - All Azure service connections use private IP addresses (no public internet traffic)

- **Network Security Groups** (NSGs):
  - App Service NSG: Allows HTTPS (443), HTTP (80) inbound
  - Private Endpoints NSG: Restrictive rules for internal traffic

### Private Endpoints DNS Configuration

**⚠️ Important:** Private endpoints require **Private DNS zones** for proper name resolution. Without DNS configuration, applications will fail to connect to Azure services via private endpoints.

**Required Private DNS Zones:**
- Cosmos DB: `privatelink.documents.azure.com`
- Redis: `privatelink.cache.azure.net`
- SignalR: `privatelink.service.signalr.net`
- App Service: `privatelink.azurewebsites.net`

**Static IP Allocation Pattern:**

Each private endpoint is assigned **deterministic static IP addresses** calculated from the Private Endpoints subnet prefix:

| Service | IP Offset | Dev IPs (10.50.8.x) | Staging IPs (10.50.8.x) | Prod IPs (10.50.8.x) | Member Names |
|---------|-----------|---------------------|-------------------------|----------------------|--------------|
| Cosmos DB (ipconfig1) | +4 | .36 | .100 | .164 | `{accountName}` |
| Cosmos DB (ipconfig2) | +5 | .37 | .101 | .165 | `{accountName}-{location}` |
| Redis | +6 | .38 | .102 | .166 | `redisEnterprise` |
| SignalR | +7 | .39 | .103 | .167 | `signalr` |
| App Service | +8 | .40 | .104 | .168 | `sites` |

**Multiple IP Configurations:**

Only **Cosmos DB** uses multiple IP configurations to support regional endpoints:
- **ipconfig1**: Primary global endpoint with static IP and account name memberName
- **ipconfig2**: Regional endpoint with static IP and `{accountName}-{location}` memberName

Redis, SignalR, and App Service each use a **single IP configuration** with generic memberNames.

**DNS Zone Setup**: Refer to [GitHub Actions documentation](../github-actions.md#private-endpoints-dns-configuration) for complete setup instructions.

### Data Architecture

**Cosmos DB Containers**:
1. **messages** - Chat messages (partition key: `/roomName`)
2. **users** - User profiles (partition key: `/userName`)
3. **rooms** - Chat rooms (partition key: `/name`)

**Consistency Level**: Session (balance of consistency, performance, availability)

**Backup Policy** (Environment-Specific):
- **Production**:
  - **Mode**: Continuous backup with 30-day retention (Continuous30Days tier)
  - **Frequency**: Automatic backups every 100 seconds
  - **Restore**: Point-in-time restore to any second within last 30 days
  - **RPO**: Near-zero (Recovery Point Objective)
  - **Cost**: ~20% additional RU/s charge (~$70/month for 4000 RU/s)
  - **Use Cases**: Accidental deletion recovery, data corruption rollback, compliance audits
- **Dev/Staging**:
  - **Mode**: Periodic backup (cost optimization)
  - **Frequency**: Every 4 hours
  - **Retention**: 8 hours
  - **Storage**: Local redundancy
  - **Cost**: No additional cost (default)

**Restore Procedures**:
1. Azure Portal → Cosmos DB account → "Point in Time Restore"
2. Select restore timestamp (production: any second within 30 days)
3. Choose scope: entire account, database, or specific container
4. Specify new account name for restored data
5. Validate and restore

See [Data Model documentation](../../architecture/data-model.md) for detailed schema information.

---

## 📁 Project Structure

```
infra/bicep/
├── main.bicep                          # Main orchestration template
├── main.parameters.dev.bicepparam      # Development environment parameters (reference only)
├── main.parameters.staging.bicepparam  # Staging environment parameters (reference only)
├── main.parameters.prod.bicepparam     # Production environment parameters (reference only)
├── modules/
│   ├── networking.bicep                # VNet, subnets, NSGs
│   ├── monitoring.bicep                # Log Analytics, Application Insights
│   ├── cosmos-db.bicep                 # Cosmos DB account + containers
│   ├── redis.bicep                     # Azure Managed Redis
│   ├── signalr.bicep                   # Azure SignalR Service
│   ├── communication.bicep             # Azure Communication Services
│   ├── translation.bicep               # Azure AI Foundry (translation services)
│   └── app-service.bicep               # App Service Plan + Web App
└── README.md                           # Source documentation (kept for local reference)
```

**Note**: Parameter files (`*.bicepparam`) are for **reference only** and document expected values. They are **NOT used** by GitHub Actions, which passes parameters directly from environment variables.

---

## 🏷️ Resource Naming Convention

Resources follow the pattern: `{resourceType}-{baseName}-{environment}-{shortLocation}`

**Examples**:
- Virtual Network: `vnet-signalrchat-dev-weu`
- App Service: `app-signalrchat-prod-weu`
- Cosmos DB: `cosmos-signalrchat-staging-weu`
- Redis Cache: `redis-signalrchat-dev-weu`

**Globally Unique Resources** (automatically get unique suffix):
- Cosmos DB: `cdb-{baseName}-{environment}-{shortLocation}`
- App Service: `app-{baseName}-{environment}-{shortLocation}`
- Communication Services: `acs-{baseName}-{environment}-{shortLocation}`

---

## 🌍 Environments

### Development (dev)

**Purpose**: Local development and feature testing

| Resource | SKU/Tier | Configuration |
|----------|----------|---------------|
| VNet | Standard | 10.50.8.0/26 |
| App Service Plan | P0V4 PremiumV4 Linux | 1 instance, .NET 9.0 |
| Cosmos DB | Autoscale (1000 RU/s max) | Single region, zone-redundant |
| Redis Cache | Balanced_B1 (Azure Managed Redis) | 2 GB |
| SignalR Service | Standard_S1 | 1 unit |
| Log Analytics | 30-day retention | 1 GB daily cap |

**Cost Estimate**: ~$150-250/month

### Staging (staging)

**Purpose**: Pre-production validation and integration testing

| Resource | SKU/Tier | Configuration |
|----------|----------|---------------|
| VNet | Standard | 10.50.8.64/26 |
| App Service Plan | P0V4 PremiumV4 Linux | 2 instances (AZ), .NET 9.0 |
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
| App Service Plan | P0V4 PremiumV4 Linux | 3 instances with AZ, .NET 9.0 |
| Cosmos DB | Autoscale (4000 RU/s max) | Multi-region (polandcentral + germanywestcentral), zone-redundant |
| Redis Cache | Balanced_B5 (Azure Managed Redis) | 12 GB |
| SignalR Service | Standard_S1 | 5 units |
| Log Analytics | 365-day retention | 10 GB daily cap |

**Cost Estimate**: ~$1200-1800/month

---

## 📦 Deployment

**All deployments are executed through GitHub Actions workflows.** Manual deployments from terminal are not supported.

### Deploy Infrastructure

See [GitHub Actions CI/CD documentation](../github-actions.md#infrastructure-deployment) for detailed deployment instructions.

**Quick Start**:
1. Go to **Actions** tab in GitHub
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Select environment (`dev`, `staging`, or `prod`)
5. Click **Run workflow**

**Deployment Time**:
- Dev: ~20-25 minutes
- Staging: ~25-35 minutes
- Production: ~25-35 minutes (includes multi-region setup)

**What Gets Deployed**:
1. Networking resource group (`rg-vnet-{baseName}-{env}-{shortLocation}`)
   - Virtual Network with 2 subnets (/27 each)
   - Network Security Groups (NSG per subnet)
   - Route Tables (per subnet)

2. Application resource group (`rg-{baseName}-{env}-{shortLocation}`)
   - App Service Plan + App Service
   - Cosmos DB NoSQL (3 containers)
   - Azure Managed Redis
   - Azure SignalR Service
   - Azure Communication Services
   - Azure AI Foundry (translation services)
   - Log Analytics + Application Insights
   - Private Endpoints (Cosmos, Redis, SignalR, App Service)

---

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

**1. App Service is running**:
```bash
az webapp show --name app-{baseName}-{env}-{shortLocation} \
  --resource-group rg-{baseName}-{env}-{shortLocation} \
  --query "state"
```

**2. Virtual Network has 2 subnets**:
```bash
az network vnet subnet list \
  --resource-group rg-vnet-{baseName}-{env}-{shortLocation} \
  --vnet-name vnet-{baseName}-{env}-{shortLocation} \
  --query "[].{Name:name, Prefix:addressPrefix}" \
  --output table
```

**3. Cosmos DB containers exist**:
```bash
az cosmosdb sql container list \
  --account-name cdb-{baseName}-{env}-{shortLocation} \
  --resource-group rg-{baseName}-{env}-{shortLocation} \
  --database-name chat \
  --query "[].name" \
  --output table
```

**4. App Service has VNet integration**:
```bash
az webapp vnet-integration list \
  --name app-{baseName}-{env}-{shortLocation} \
  --resource-group rg-{baseName}-{env}-{shortLocation}
```

**5. Application Insights connected**:
```bash
az monitor app-insights component show \
  --app appins-{baseName}-{env}-{shortLocation} \
  --resource-group rg-{baseName}-{env}-{shortLocation} \
  --query "connectionString"
```

---

## 🔧 Troubleshooting

### Common Issues

#### 1. GitHub Actions Workflow Fails - Authentication Error

**Cause**: Missing or incorrect Azure credentials in GitHub secrets.

**Solution**: 
- Verify `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` are set in GitHub environment
- Ensure federated credentials are configured for GitHub Actions
- Check service principal has Contributor role

See [GitHub Secrets Setup](../github-secrets.md) for configuration guide.

#### 2. Deployment Fails with "Resource name already taken"

**Cause**: Cosmos DB, App Service, or Communication Services names must be globally unique.

**Solution**: Modify `BICEP_BASE_NAME` variable in GitHub environment. Bicep automatically adds unique suffix to globally-scoped resources.

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

---

## 📚 Additional Resources

- [GitHub Actions for Azure](https://learn.microsoft.com/azure/developer/github/github-actions)
- [Azure Bicep Best Practices](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices)
- [Azure Well-Architected Framework](https://learn.microsoft.com/azure/well-architected/)
- [Cosmos DB Best Practices](https://learn.microsoft.com/azure/cosmos-db/nosql/best-practice-dotnet)
- [App Service Best Practices](https://learn.microsoft.com/azure/app-service/app-service-best-practices)
- [Azure SignalR Service Documentation](https://learn.microsoft.com/azure/azure-signalr/)

---

**Related Documentation**:
- [GitHub Actions CI/CD](../github-actions.md) - Deployment workflows
- [Data Model](../../architecture/data-model.md) - Cosmos DB schema
- [Bootstrap Guide](../bootstrap.md) - Complete deployment from scratch
- [Production Checklist](../production-checklist.md) - Pre-launch verification

**Source**: `infra/bicep/README.md` (kept for local reference)  
**Last Updated**: 2025-12-02
