# GitHub Actions CI/CD Pipelines

This repository uses GitHub Actions for continuous integration and deployment to Azure App Service.

## 📋 Pipeline Overview

### 1. CI - Build and Test (`ci.yml`)
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

### 2. CD Staging (`cd-staging.yml`)
**Triggers:**
- Push to `main` branch (after PR merge)

**Purpose:** Automatically deploy to staging environment

**Jobs:**
- **Build:** Full CI pipeline (build + test)
- **Deploy:** Deploy to Azure App Service (staging environment)

**Environment:** `staging` (no required reviewers)

### 3. CD Production (`cd-production.yml`)
**Triggers:**
- Push tags matching `v*.*.*` (e.g., `v1.0.0`, `v2.1.3`)

**Purpose:** Deploy to production with approval gate

**Jobs:**
- **Build:** Full CI pipeline (build + test)
- **Deploy:** Deploy to Azure App Service (production environment)

**Environment:** `production` (requires reviewer approval)

**Retention:** Production artifacts kept for 30 days

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
**staging environment:**
```
AZURE_WEBAPP_NAME       # Azure App Service name for staging (e.g., chat-dev-plc)
```

**production environment:**
```
AZURE_WEBAPP_NAME       # Azure App Service name for production (e.g., chat-prod-plc)
```

### Azure Setup

#### 1. Create Federated Credentials in Azure
In your Service Principal's **Certificates & secrets → Federated credentials**:

**For Staging:**
- Name: `github-staging`
- Issuer: `https://token.actions.githubusercontent.com`
- Subject identifier: `repo:smereczynski/SignalR-Chat:environment:staging`
- Audience: `api://AzureADTokenExchange`

**For Production:**
- Name: `github-production`
- Issuer: `https://token.actions.githubusercontent.com`
- Subject identifier: `repo:smereczynski/SignalR-Chat:environment:production`
- Audience: `api://AzureADTokenExchange`

#### 2. Grant Permissions
Ensure your Service Principal has **Contributor** role on:
- Staging App Service
- Production App Service

## 🌍 GitHub Environments

### staging
- **Protection rules:** None (auto-deploy on push to main)
- **Variables:** `AZURE_WEBAPP_NAME` (staging app service name)

### production
- **Protection rules:**
  - ✅ Required reviewers: 1-2 people
  - ✅ Branch restriction: Only `main` branch tags
- **Variables:** `AZURE_WEBAPP_NAME` (production app service name)

## 🚀 Deployment Workflow

### Deploy to Staging
1. Create a feature branch: `git checkout -b feature/my-feature`
2. Make changes and commit
3. Push and create PR to `main`
4. Wait for CI pipeline ✅
5. Wait for SonarQube quality gate ✅
6. Wait for CodeQL security scan ✅
7. Merge PR → **Automatic deployment to staging** 🚀

### Deploy to Production
1. Ensure staging is stable and tested
2. Create a version tag:
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
  contents: read        # Read repository code
  id-token: write       # Required for Azure OIDC federation
```

## 🔍 Quality Gates

The following checks run automatically on PRs:
- ✅ GitHub Actions CI (build + tests)
- ✅ SonarQube quality gate
- ✅ CodeQL security analysis

PRs cannot be merged until all checks pass.

## 📦 Artifact Management

- **CI builds:** Artifacts retained for 7 days
- **Staging deployments:** Artifacts retained for 7 days
- **Production deployments:** Artifacts retained for 30 days

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
