# GitHub Actions CI/CD Workflows

**Status**: P1 (Created Nov 21, 2025)
**Workflows**: `ci.yml`, `cd.yml`, `deploy-infrastructure.yml`

This document explains the automated pipelines for **build**, **test**, **deployment**, and **infrastructure lifecycle** using GitHub Actions.

---
## 1. Overview
| Workflow | Purpose | Trigger | Environments |
|----------|---------|---------|--------------|
| `CI - Build and Test (ci.yml)` | Compile & test on every push / PR | `push` (all branches), `pull_request` (main) | N/A (runs in ephemeral runner) |
| `CD - Continuous Deployment (cd.yml)` | Build & deploy application code | `push` to `main`, tags `rc*` / `v*.*.*`, manual dispatch | dev / staging / prod |
| `Deploy Infrastructure (deploy-infrastructure.yml)` | Validate / deploy / teardown Azure resources | Manual dispatch (`validate`, `deploy`, `teardown`*) | dev / staging / prod |

*Note: `teardown` action is blocked for `prod` environment as a safety measure.

---
## 2. Secrets & Variables
### 2.1 Required GitHub Secrets
| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Federated identity app registration client ID |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription for deployments |
| `ENTRA_ID_CLIENT_ID` | Entra ID app (for infrastructure Bicep param) |
| `ENTRA_ID_CLIENT_SECRET` | Client secret (used during infra deployment) |
| `ENTRA_ID_CONNECTION_STRING` | Optional consolidated connection string (`ClientId=...;ClientSecret=...`) |
| `OTP_PEPPER` | Pepper for OTP hashing (security) |

### 2.2 GitHub Action Variables (Environment Scoped)
| Variable | Example | Notes |
|----------|---------|-------|
| `BICEP_BASE_NAME` | `signalrchat` | Base name prefix for all Azure resources |
| `BICEP_LOCATION` | `westeurope` | Full Azure region name |
| `BICEP_SHORT_LOCATION` | `weu` | Short region suffix used in resource names |
| `BICEP_VNET_ADDRESS_PREFIX` | `10.50.0.0/20` | Virtual network CIDR |
| `BICEP_APP_SERVICE_SUBNET_PREFIX` | `10.50.1.0/24` | App Service subnet |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | `10.50.2.0/24` | Private endpoints subnet |
| `BICEP_VNET_DNS_SERVERS` | `[]` or `['10.0.0.4']` | Custom DNS servers (JSON) |
| `BICEP_ACS_DATA_LOCATION` | `westeurope` | ACS data location |
| Entra ID flags | Various | Policies: SSO attempt, fallback OTP, tenant validation |

Set per environment (dev/staging/prod) in: **Settings → Environments → <env> → Variables**.

---
## 3. CI Workflow (`ci.yml`)
### 3.1 Steps Summary
1. Checkout
2. Setup .NET 9 / Node.js 20
3. Install npm dependencies (`npm ci`)
4. Build frontend assets (`npm run build:prod`)
5. Clean Chat.Web build artifacts (ensures fresh resource compilation – translations)
6. Restore & build .NET solution (Release)
7. List test assemblies for diagnostics
8. Run tests (in-memory mode with `Testing__InMemory=true`)
9. Upload test results artifact (trx)
10. Publish & upload build artifacts when pushing to `main` or tags

### 3.2 Test Mode
Forced in-memory dependencies ensure deterministic test behavior:
```yaml
env:
  Testing__InMemory: "true"
```
Prevents CI from needing external cloud resources.

### 3.3 Artifacts
| Name | Contents | Retention |
|------|----------|-----------|
| `test-results` | `*.trx` | 7 days |
| `webapp-package` | Published site output | 7 days (CI), 30 days (CD) |

### 3.4 Failure Diagnostics
If tests fail, open artifact `test-results` → download `.trx` → inspect failing test output in IDE or browser.

---
## 4. Continuous Deployment (`cd.yml`)
### 4.1 Environment Determination Logic
| Trigger | Environment | Release Flag |
|---------|-------------|--------------|
| Push to `main` | `dev` | false |
| Tag `rc*` | `staging` | false |
| Tag `v*.*.*` | `prod` | true |
| Manual `workflow_dispatch` | Selected input | false |

### 4.2 Jobs
1. `determine-environment` – sets `environment` + `is-release` outputs.
2. `build` – identical build & test pipeline as CI (Release mode).
3. `deploy` – downloads artifact & deploys to Azure App Service via OIDC.
4. `release` – creates GitHub Release (prod tags only) using `gh release create`.

### 4.3 Deployment App Name Convention
```text
<baseName>-<environment>-<shortLocation>
# Example: signalrchat-dev-weu
```
Matches infrastructure outputs.

### 4.4 Post-Deploy Validation
- Wait 30s for warmup.
- Simple HTTP status check against `/health`.
- Summary appended to `GITHUB_STEP_SUMMARY`.

### 4.5 Production Release Process
Create tag:
```bash
git tag v1.0.0
git push origin v1.0.0
```
Workflow builds, deploys to production, then creates a GitHub Release referencing changelog diff.

---
## 5. Infrastructure Deployment (`deploy-infrastructure.yml`)
### 5.1 Actions Supported
| Action | Description |
|--------|-------------|
| `validate` | Syntax check + template validation + what-if preview |
| `deploy` | Full resource provisioning + outputs capture + health pre-check |
| `teardown` | Selective resource deletion (preserves networking, ACS, monitoring) |

### 5.2 Resource Groups
| Type | Pattern |
|------|---------|
| Application RG | `rg-<baseName>-<env>-<shortLocation>` |
| Networking RG | `rg-vnet-<baseName>-<env>-<shortLocation>` |

### 5.3 Provider Registration
Workflow ensures required providers are registered:
- DocumentDB, Cache (RedisEnterprise), SignalRService, Communication, Web, Network, Insights, OperationalInsights, AlertsManagement.

### 5.4 Bicep Parameters
Parameters passed explicitly to avoid stale defaults. Sensitive values resolved from secrets (e.g. `entraIdClientSecret`).

### 5.5 Outputs
Deployment writes JSON to `deployment-output.json` then extracts `appUrl` for summary.

### 5.6 Post-Deployment Validation
Health endpoint call (`/health`) after short delay. Cosmos DB seeding performed by application startup.

### 5.7 Teardown Safety
**Production Protection**: Teardown action is **blocked for `prod` environment** to prevent accidental data loss. Production resources must be deleted manually via Azure Portal or Azure CLI.

**Selective Deletion**: Teardown performs **selective resource deletion** rather than full resource group deletion. This preserves:
- **Networking infrastructure**: VNet, Subnets, NSGs, Private DNS Zones (entire networking RG untouched)
- **Azure Communication Services**: CommunicationServices and EmailServices resources (avoids domain re-verification)
- **Application Insights**: Telemetry and monitoring history preserved
- **Log Analytics Workspace**: Logs and analytics data retained for compliance/audit

**Deleted Resources**: App Service, Cosmos DB, Redis Cache, SignalR Service

**Resource Groups**: Both application and networking RGs remain intact (not deleted).

---
## 6. Security & Compliance
| Area | Practice |
|------|----------|
| Authentication | OIDC federation (no stored Azure service principals) |
| Secret Scope | GitHub Actions secrets limited to required values (OTP pepper, Entra ID credentials) |
| Least Privilege | Deployment uses subscription-scoped privileges; consider narrowing to RG scope later |
| Change Control | Production deploy only via tags `v*.*.*` (immutable) |

---
## 7. Troubleshooting
| Symptom | Cause | Resolution |
|---------|-------|-----------|
| App deploy fails (CD) | Missing secrets | Verify `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` |
| Infra validate error | Missing environment variables | Environment-scoped variables not configured – check Actions Environments |
| Release creation fails | `gh` CLI permission / token issues | Ensure `contents: write` permission present |
| Health check warning after deploy | App cold start or seeding still running | Re-check in 30–60s, inspect App Service logs |
| What-If returns unexpected deletions | Parameter mismatch | Re-run `validate`, confirm environment variables & Bicep parameters |
| Redis/Cosmos health check failing | Connection strings absent in App Service | Run infrastructure deployment again (deploy mode) | 

---
## 8. Local Reproduction
Simulate build locally:
```bash
# Frontend
npm ci
npm run build:prod

# .NET build & tests
rm -rf src/Chat.Web/bin src/Chat.Web/obj
Testing__InMemory=true dotnet build src/Chat.sln --configuration Release
Testing__InMemory=true dotnet test src/Chat.sln --configuration Release --no-build
```
Package publish output:
```bash
dotnet publish src/Chat.Web/Chat.Web.csproj -c Release -o ./publish
```

---
## 9. Operational Playbooks
| Scenario | Steps |
|----------|-------|
| Hotfix (dev) | Push commit to `main` → auto deploy to dev |
| Staging candidate | Create tag `rc1.2.3` → deploy to staging |
| Production release | Create tag `v1.2.3` → deploy & GitHub Release |
| Infra param change | Run infrastructure workflow with `validate` then `deploy` |
| Selective resource cleanup | Run infrastructure workflow with `teardown` (dev/staging only, preserves networking/ACS/monitoring) |

---
## 10. Enhancement Backlog
| Item | Reason |
|------|-------|
| Add code coverage upload | Quality visibility |
| Add link checker job | Documentation integrity |
| Add security scanning (Dependabot, CodeQL) | Supply chain hygiene |
| Parallel test splitting | Speed on large test suites |
| Smoke tests post-deploy | Validate critical endpoints beyond `/health` |

---
## 11. References
- `.github/workflows/ci.yml`
- `.github/workflows/cd.yml`
- `.github/workflows/deploy-infrastructure.yml`
- `infra/bicep/main.bicep`
- `docs/deployment/bootstrap.md`

---
**Last Updated**: 2025-11-28
