# GitHub Actions CI/CD Pipelines

This repository uses GitHub Actions for continuous integration, infrastructure deployment, and application deployment to Azure.

## 📋 Pipeline Overview

### 1. Infrastructure Deployment (`deploy-infrastructure.yml`)
**Triggers:**
- Manual workflow dispatch (via GitHub UI or CLI)
- Supports dev, staging, and production environments

**Purpose:** Deploy and manage Azure infrastructure using Bicep templates

**Jobs:**
- **Setup:** Configure Azure credentials and validate environment
- **What-If:** Preview infrastructure changes before deployment
- **Approval:** Manual approval gate for production deployments
- **Deploy:** Execute Bicep deployment to Azure
- **Validate:** Verify deployment (check 2 subnets exist in VNet)
- **Seed Database:** Automatically seed Cosmos DB with initial data

**Required Environment Variables** (per environment):
```
BICEP_BASE_NAME                        # Base name for resources (e.g., signalrchat)
BICEP_LOCATION                         # Azure region (e.g., polandcentral)
BICEP_SHORT_LOCATION                   # Short location code (e.g., plc for polandcentral)
BICEP_VNET_ADDRESS_PREFIX             # VNet CIDR /26 (e.g., 10.0.0.0/26)
BICEP_APP_SERVICE_SUBNET_PREFIX       # First subnet /27 (e.g., 10.0.0.0/27)
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX # Second subnet /27 (e.g., 10.0.0.32/27)
BICEP_ACS_DATA_LOCATION              # ACS data location (e.g., Europe)
```

**Deployment Time:**
- Dev/Staging: ~20-25 minutes
- Production: ~25-35 minutes (includes multi-region setup)

**Resources Deployed:**

**Networking Resource Group** (`rg-vnet-{baseName}-{env}-{shortLocation}`):
- Virtual Network with 2 subnets (/27 each)
- Network Security Groups (NSG per subnet: `nsg-{subnetName}`)
- Route Tables (per subnet: `rt-{vnetName}-appservice`, `rt-{vnetName}-pe`)

**Application Resource Group** (`rg-{baseName}-{env}-{shortLocation}`):
- App Service Plan (P0V4 PremiumV4)
- App Service (Web App with VNet integration)
- Cosmos DB NoSQL (3 containers: messages, users, rooms)
- Azure Managed Redis (Balanced_B1/B3/B5)
- Azure SignalR Service (Standard_S1)
- Azure Communication Services
- Log Analytics Workspace + Application Insights
- Private Endpoints in networking subnet (Cosmos DB, Redis, SignalR)

### 2. CI - Build and Test (`ci.yml`)
**Triggers:**
- Push to any branch
- Pull requests to `main`

**Purpose:** Validate code quality, run tests, and create deployable artifacts

**Jobs:**
- ✅ Restore npm & dotnet dependencies
- ✅ Build frontend assets (CSS + JS bundles via Sass & esbuild)
- ✅ Build .NET solution
- ✅ Run all tests (unit + integration)
- ✅ Upload test results
- ✅ Create and upload build artifacts (only for `main` branch and tags)

### 3. CD - Continuous Deployment (`cd.yml`)
**Triggers:**
- Push to `main` branch → Deploy to **dev**
- Push tags matching `rc*` (e.g., `rc1.0.0`) → Deploy to **staging**
- Push tags matching `v*.*.*` (e.g., `v1.0.0`) → Deploy to **prod** + Create GitHub Release

**Purpose:** Unified deployment pipeline with environment promotion

**Jobs:**
- **Determine Environment:** Automatically select target environment based on trigger
- **Build:** Full CI pipeline (build + test)
- **Deploy:** Deploy to Azure App Service (dev/staging/prod)
- **Release:** Create GitHub Release (production only)

**Environment Selection:**
- `main` branch → `dev` (automatic, no approval)
- `rc*` tags → `staging` (optional approval)
- `v*.*.*` tags → `prod` (required approval)

**Artifact Retention:** 30 days for all environments

## 🔐 Authentication

All pipelines use **Azure Federated Identity (OIDC)** - no secrets stored in GitHub!

### Required GitHub Variables

#### Repository Variables (Settings → Secrets and variables → Actions → Variables):
```
AZURE_CLIENT_ID         # Service Principal Application (client) ID
AZURE_TENANT_ID         # Your Azure AD Tenant ID
AZURE_SUBSCRIPTION_ID   # Your Azure Subscription ID
```

#### Environment Variables:

**All environments (dev/staging/prod) - for both infrastructure and application deployment:**
```
BICEP_BASE_NAME                        # Base name for resources (e.g., chatapp)
BICEP_LOCATION                         # Azure region (e.g., westeurope)
BICEP_SHORT_LOCATION                   # Short location code (e.g., weu for westeurope)
BICEP_VNET_ADDRESS_PREFIX             # VNet CIDR /26 (e.g., 10.0.0.0/26, 10.1.0.0/26, 10.2.0.0/26)
BICEP_APP_SERVICE_SUBNET_PREFIX       # First subnet /27 (e.g., 10.0.0.0/27, 10.1.0.0/27, 10.2.0.0/27)
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX # Second subnet /27 (e.g., 10.0.0.32/27, 10.1.0.32/27, 10.2.0.32/27)
BICEP_ACS_DATA_LOCATION              # ACS data location (e.g., Europe)
```

**Note:** App Service name is automatically constructed as `app-{BICEP_BASE_NAME}-{environment}-{BICEP_SHORT_LOCATION}` matching Bicep naming convention.

### Azure Setup

#### 1. Create Federated Credentials in Azure
In your Service Principal's **Certificates & secrets → Federated credentials**:

**For Development (infrastructure only):**
- Name: `github-dev`
- Issuer: `https://token.actions.githubusercontent.com`
- Subject identifier: `repo:smereczynski/SignalR-Chat:environment:dev`
- Audience: `api://AzureADTokenExchange`

**For Staging:**
- Name: `github-staging`
- Issuer: `https://token.actions.githubusercontent.com`
- Subject identifier: `repo:smereczynski/SignalR-Chat:environment:staging`
- Audience: `api://AzureADTokenExchange`

**For Production:**
- Name: `github-prod`
- Issuer: `https://token.actions.githubusercontent.com`
- Subject identifier: `repo:smereczynski/SignalR-Chat:environment:prod`
- Audience: `api://AzureADTokenExchange`

#### 2. Grant Permissions
Ensure your Service Principal has:
- **Contributor** role on the Azure subscription (for infrastructure deployment)
- **Website Contributor** role on App Services (for application deployment)

## 🌍 GitHub Environments

### dev
- **Purpose:** Development environment (infrastructure + application deployment)
- **Protection rules:** None (auto-deploy on push to `main`)
- **Variables:** 7 BICEP_* variables

### staging
- **Purpose:** Staging environment (infrastructure + application deployment)
- **Protection rules:** Optional reviewers
- **Trigger:** Tag with `rc*` pattern (e.g., `rc1.0.0`)
- **Variables:** 7 BICEP_* variables

### prod
- **Purpose:** Production environment (infrastructure + application deployment)
- **Protection rules:**
  - ✅ Required reviewers: 1-2 people
  - ✅ Branch restriction: Only tags from `main` branch
- **Trigger:** Tag with `v*.*.*` pattern (e.g., `v1.0.0`)
- **Variables:** 7 BICEP_* variables

## 🚀 Deployment Workflow

### Deploy Infrastructure (First Time Setup or Updates)

**Via GitHub UI:**
1. Go to **Actions** tab
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Select environment: `dev`, `staging`, or `prod`
5. Click **Run workflow** button
6. Monitor deployment progress (~20-30 minutes)

**Via GitHub CLI:**
```bash
# Deploy to development
gh workflow run deploy-infrastructure.yml -f environment=dev

# Deploy to staging
gh workflow run deploy-infrastructure.yml -f environment=staging

# Deploy to production (requires approval)
gh workflow run deploy-infrastructure.yml -f environment=prod
```

**Prerequisites:**
- Configure 7 environment variables in GitHub (see Infrastructure Deployment section above)
- Azure federated credentials configured for the Service Principal
- Service Principal has Contributor role on subscription or resource group

**What Happens:**
1. ✅ Azure login via OIDC
2. ✅ Create networking resource group (`rg-vnet-{baseName}-{env}-{shortLocation}`)
3. ✅ Create application resource group (`rg-{baseName}-{env}-{shortLocation}`)
4. ✅ Bicep what-if analysis (preview changes)
5. ⏸️ Manual approval (production only)
6. ✅ Deploy networking infrastructure (VNet, NSGs, Route Tables) to networking RG
7. ✅ Deploy application resources to application RG with cross-RG VNet references
8. ✅ Validate deployment (check 2 subnets in VNet)
9. ✅ Seed database with initial data
10. ✅ Output app URL and connection details

### Deploy Application to Development
1. Create a feature branch: `git checkout -b feature/my-feature`
2. Make changes and commit
3. Push and create PR to `main`
4. Wait for CI pipeline ✅
5. Merge PR → **Automatic deployment to dev** 🚀

### Deploy Application to Staging
1. Ensure dev is stable and tested
2. Create a release candidate tag:
   ```bash
   git checkout main
   git pull
   git tag -a rc1.0.0 -m "Release candidate 1.0.0"
   git push origin rc1.0.0
   ```
3. GitHub Actions workflow starts
4. Build and test complete ✅
5. (Optional) Manual approval ⏸️
6. **Deploy to staging** 🚀

### Deploy Application to Production
1. Ensure staging is stable and tested
2. Create a production release tag:
   ```bash
   git checkout main
   git pull
   git tag -a v1.0.0 -m "Release version 1.0.0"
   git push origin v1.0.0
   ```
3. GitHub Actions workflow starts
4. Build and test complete ✅
5. **Manual approval required** ⏸️
6. After approval → **Deploy to production** 🚀
7. **GitHub Release created automatically** 📦

## 📊 Pipeline Permissions

### CI Pipeline
```yaml
permissions:
  contents: read        # Read repository code
  pull-requests: read   # Read PR information
```

### CD Pipelines (Staging & Production)
```yaml
permissions:
  contents: write       # Required for creating GitHub releases (prod only)
  id-token: write       # Required for Azure OIDC federation
```

## 🔍 Quality Gates

The following checks run automatically on PRs:
- ✅ GitHub Actions CI (build + tests)

PRs cannot be merged until all checks pass.

## 📦 Artifact Management

- **CI builds:** Artifacts retained for 7 days
- **CD deployments:** Artifacts retained for 30 days (all environments)

## 🛠️ Technology Stack

- **.NET:** 9.0.x
- **Node.js:** 20.x
- **Frontend:** Sass + esbuild
- **Testing:** xUnit with .trx output
- **Deployment:** Azure App Service (Windows)

## 📝 Notes

- All workflows use latest action versions (@v4, @v3, @v2)
- npm uses `ci` instead of `install` for reproducible builds
- Build artifacts are cached between jobs
- Azure logout is always executed (even on failure)
- Production deployments include deployment summary in GitHub
