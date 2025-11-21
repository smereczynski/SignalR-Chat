# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **Documentation Accuracy Corrections** (2025-01-21):
  - ✅ Fixed **incorrect in-memory mode documentation** - Previously claimed in-memory mode was "default" when running `dotnet run`, but app actually connects to Azure if `.env.local` exists
  - ✅ Corrected all documentation to require `Testing__InMemory=true` environment variable for true in-memory mode
  - ✅ Updated `docs/development/local-setup.md`:
    - Removed misleading "In-Memory Mode (Default)" heading
    - Added explicit requirement for `Testing__InMemory=true` environment variable
    - Added troubleshooting section: "Application Connects to Azure When I Expected In-Memory Mode"
    - Added verification steps to confirm in-memory vs Azure mode
    - Added note about Entra ID requiring HTTPS and proper app registration for local development
  - ✅ Updated `docs/reference/faq.md`:
    - Corrected misleading "Do I need Azure? No!" answer with accurate `Testing__InMemory=true` requirement
    - Updated mode switching instructions with correct commands
    - Added new FAQ entry: "Does Entra ID (SSO) work for local development?" explaining HTTPS/redirect URI requirements
    - Added verification steps to determine which mode is active
  - ✅ Updated `docs/getting-started/quickstart.md`:
    - Changed Step 2 command to include required `Testing__InMemory=true` environment variable
    - Added warning about Azure connection attempts without the flag
    - Clarified "What's Running?" section with explicit mode differences
  - ⚠️ **Important**: Users following old documentation would have experienced unexpected Azure connection attempts if `.env.local` existed
  - 🔍 **Root Cause**: Application runtime testing revealed `.env.local` file presence causes Azure resource connections regardless of intent
  - ✅ **Verification**: Tested both modes - confirmed `Testing__InMemory=true` eliminates all Azure connections (no Cosmos DB, SignalR Service, or Redis)

### Added
- **P0 Critical Documentation** (2025-01-21):
  - ✅ `docs/development/local-setup.md` - Comprehensive development environment setup guide
    - In-memory vs Azure mode comparison
    - IDE setup (VS Code, Visual Studio) with debugging configuration
    - Frontend development (npm, esbuild, sass, hot reload)
    - Running tests in different modes
    - Local Redis and Cosmos DB Emulator setup
    - Common development tasks (add language, SignalR methods, REST endpoints)
    - Troubleshooting and performance tips
  - ✅ `docs/development/testing.md` - Complete testing guide with issue #113 explanation
    - Test structure (179 tests: 9 unit, 135 integration, 35 web)
    - Running tests in in-memory and Azure modes
    - Test projects overview with code examples
    - Known issues section (SignalR test failures explained)
    - Writing tests (templates, best practices, parameterized tests)
    - CustomWebApplicationFactory deep dive
    - Debugging tests (VS Code, Visual Studio, CLI)
    - CI/CD testing, code coverage, test performance
  - ✅ `docs/reference/faq.md` - Comprehensive FAQ covering all major topics
    - General (what is SignalR Chat, features, technologies)
    - Development (in-memory mode, Azure mode, adding languages/users/rooms)
    - Testing (issue #113 root cause, running tests without Azure)
    - Azure & Deployment (resources, costs, Bicep, environments, Windows vs Linux)
    - Authentication & Security (OTP flow, password alternatives, security headers, log sanitization)
    - Performance & Scalability (capacity, horizontal scaling, Cosmos DB optimization, monitoring)
    - Troubleshooting (port conflicts, OTP delivery, SignalR failures, Cosmos DB, Redis, CI/CD)
  - ✅ `docs/getting-started/installation.md` - Full Azure setup guide
    - Installation modes comparison (in-memory vs Azure)
    - Prerequisites (Azure CLI, .NET 9, Git)
    - Automated installation via GitHub Actions (step-by-step)
    - Manual installation via Azure CLI + Bicep
    - Azure resources overview (with cost estimates: $40-50/month dev)
    - Configuration guide (app settings, environment variables, Linux vs Windows)
    - Email OTP configuration (Azure Communication Services setup)
    - Health checks (application and component health verification)
    - Troubleshooting common installation issues
  - 📊 **Documentation Completion**: Moved from 33% to 41% (27/68 planned files)

### Documentation
- **Documentation Structure Issues Identified**:
  - ⚠️ **Missing Files**: Multiple documentation files referenced but not yet created:
    - `docs/getting-started/installation.md` - Referenced in README and getting-started/README.md
    - `docs/deployment/azure.md` - Referenced in README and deployment/README.md
    - `docs/deployment/azure/` directory - Referenced in multiple locations
    - `docs/deployment/github-actions.md` - Referenced in README and deployment/README.md
    - `docs/deployment/environments.md` - Referenced in deployment/README.md
    - `docs/deployment/troubleshooting.md` - Referenced in deployment/README.md
    - `docs/architecture/data-model.md` - Referenced in README, docs/README.md, and architecture/overview.md
    - `docs/architecture/security.md` - Referenced in README, docs/README.md, and architecture/overview.md
    - `docs/architecture/diagrams/` directory - Referenced in docs/README.md and architecture/overview.md
    - `docs/features/real-time-messaging.md` - Referenced in README and docs/README.md
    - `docs/features/read-receipts.md` - Referenced in README and docs/README.md
    - `docs/features/notifications.md` - Referenced in README and docs/README.md
    - `docs/features/localization.md` - Referenced in README and docs/README.md
    - `docs/features/rate-limiting.md` - Referenced in README and docs/README.md
    - `docs/features/pagination.md` - Referenced in README and docs/README.md
    - `docs/development/local-setup.md` - Referenced in README, docs/README.md, and getting-started/README.md
    - `docs/development/project-structure.md` - Referenced in docs/README.md
    - `docs/development/testing.md` - Referenced in README and docs/README.md
    - `docs/development/debugging.md` - Referenced in docs/README.md
    - `docs/development/vscode-setup.md` - Referenced in docs/README.md
    - `docs/operations/monitoring.md` - Referenced in README, docs/README.md, and deployment/README.md
    - `docs/operations/opentelemetry.md` - Referenced in docs/README.md
    - `docs/operations/application-insights.md` - Referenced in docs/README.md
    - `docs/operations/logging.md` - Referenced in docs/README.md
    - `docs/operations/diagnostics.md` - Referenced in docs/README.md
    - `docs/operations/health-checks.md` - Referenced in docs/README.md
    - `docs/operations/performance.md` - Referenced in docs/README.md
    - `docs/reference/` entire directory missing - Referenced extensively in README and docs/README.md
    - `docs/reference/api/rest-endpoints.md` - Referenced in docs/README.md
    - `docs/reference/api/signalr-hub.md` - Referenced in docs/README.md
    - `docs/reference/configuration-reference.md` - Referenced in docs/README.md
    - `docs/reference/telemetry-reference.md` - Referenced in docs/README.md
    - `docs/reference/faq.md` - Referenced in docs/README.md and getting-started/README.md
    - `docs/reference/glossary.md` - Referenced in docs/README.md
  - ⚠️ **Broken Cross-References**: Several internal links point to non-existent files
  - ⚠️ **Inconsistent Structure**: Some sections are well-developed (deployment, architecture) while others are missing
  - ✅ **Existing Documentation**:
    - `docs/getting-started/` - Partially complete (README, quickstart, configuration)
    - `docs/architecture/` - Core files exist (overview, system-design, 3 ADRs)
    - `docs/deployment/` - Well-developed (bootstrap, github-secrets, github-variables, production-checklist, windows-to-linux-migration)
    - `docs/features/` - Partial (authentication, presence, sessions, README)
    - `docs/development/` - Two specialized guides (entra-id, admin-panel)
    - `docs/operations/` - Only disaster-recovery.md exists
  - 📋 **Documentation Plan**: `docs/DOCUMENTATION-PLAN.md` exists with comprehensive roadmap for missing content
  - 🔗 **README.md**: Well-structured but contains many broken internal links to planned documentation

### Fixed
- **Documentation Issues Identified for Future Resolution**:
  - Issue #113 documented: SignalR integration tests fail without Azure SignalR Service (environment-dependent, not a code bug)
  - TestAuthHandler not invoked for SignalR connections in local testing
  - Tests pass with `.env.local` (Azure SignalR) but fail without it
  - Recommendation: Document as expected behavior, require Azure resources for full test suite

### Added
- **Microsoft Entra ID Multi-Tenant Authentication** (#101)
  - **Dual Authentication**: Users can authenticate via Microsoft Entra ID (Azure AD) OR OTP
  - **UPN-Based Authorization**: Strict user lookup by User Principal Name (UPN) from Entra ID token
  - **Multi-Tenant Support**: Configured for `organizations` (any Entra ID tenant)
  - **Security**:
    - `AllowedTenants` list for tenant validation (CRITICAL for multi-tenant security)
    - Token validation: signature, issuer, audience, expiration
    - UPN claim extraction from `preferred_username` token claim
    - **Strict UPN matching**: No auto-provisioning or fallback to email/username
  - **User Management**:
    - **Pre-population required**: Admin MUST set `Upn` field in database before user's first Entra ID login
    - Automatic user profile update on Entra ID login:
      - **FullName synchronized with DisplayName** (chat displays Entra ID name)
      - UPN, TenantId, Email updated from token claims
      - **Country and Region** populated from `country` and `state` token claims
    - `GetByUpn()` repository method for UPN-based user lookup
    - Users without matching UPN are denied access (redirected to OTP if `OtpForUnauthorizedUsers: true`)
  - **User Model Enhancements**:
    - `DisplayName`: Entra ID display name from token (may differ from FullName)
    - `Country`: ISO 3166-1 alpha-2 country code (e.g., "US", "PL")
    - `Region`: State/province from Entra ID (e.g., "California", "Mazowieckie")
    - **Chat Display**: Shows `FullName` (synced with Entra ID `DisplayName` on login, not seeded name)
  - **Fallback Options**:
    - `EnableOtp`: Always enabled for OTP fallback
    - `OtpForUnauthorizedUsers`: Redirect unauthorized Entra ID users to OTP login
  - **Configuration**:
    - `EntraId__ClientId`, `EntraId__ClientSecret`: App registration credentials
    - `EntraId__Authorization__AllowedTenants`: List of authorized tenant IDs
    - `EntraId__Authorization__RequireTenantValidation`: Enforce tenant restrictions
    - Connection string support: `ConnectionStrings__EntraId="ClientId=<id>;ClientSecret=<secret>"`
  - **UI Changes**:
    - Login page: "Sign in with Microsoft" button with Microsoft logo
    - Divider: "or use one-time password" between authentication options
    - Both authentication methods always visible (not either/or)
  - **Documentation**: Comprehensive multi-tenant setup guide in `/docs/development/entra-id-multi-tenant-setup.md`
  - **NuGet Packages**: Microsoft.Identity.Web 4.0.1, Microsoft.Identity.Web.UI 4.0.1

### Security
- **CORS Origin Validation for SignalR**: Implemented comprehensive origin validation to prevent CSRF attacks on `/chatHub` endpoint (#63)
  - **CORS Policy**: Browser-enforced CORS policy named "SignalRPolicy"
    - Validates `Origin` header on SignalR negotiate and connection requests
    - Environment-specific configuration via `appsettings.{Environment}.json`
    - Development: `AllowAllOrigins: true` with localhost whitelist for easier debugging
    - Staging/Production: `AllowAllOrigins: false` with environment-specific allowed origins
    - Startup validation: throws if `AllowAllOrigins: true` in Production
  - **Hub Filter Defense**: Server-side `OriginValidationFilter` (defense-in-depth)
    - Validates `Origin` and `Referer` headers on hub connection and method invocation
    - Blocks unauthorized connections even if CORS policy bypassed
    - Logs security warnings for blocked attempts with origin details
  - **Configuration**:
    - `Cors__AllowedOrigins`: Array of allowed origins (e.g., `["https://app-signalrchat-prod-weu.azurewebsites.net"]`)
    - `Cors__AllowAllOrigins`: Boolean to allow all origins (dev only)
    - Azure App Service: Set via environment variables `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc.
  - **Testing**: 5 integration tests covering allowed origins, blocked origins, preflight requests, same-origin, and health check endpoints
  - **Documentation**: ADR 0001 documents decision rationale and alternatives considered

### Changed
- **Azure App Service Platform Migration**: Migrated from Windows to Linux App Service (#TBD)
  - **Platform**: Changed from Windows to Linux App Service (.NET 9.0 on Linux)
  - **Bicep Changes**:
    - App Service Plan: `kind: 'linux'` with `reserved: true` property
    - Web App: `kind: 'app,linux'` with `linuxFxVersion: 'DOTNETCORE|9.0'`
    - Removed Windows-only Application Insights agent extensions (9 settings)
    - Changed app settings notation from colon (`:`) to double underscore (`__`) for Linux compatibility
  - **Configuration Changes**:
    - Linux requires `__` notation in Bicep: `Cosmos__Database` (not `Cosmos:Database`)
    - ASP.NET Core automatically translates `__` → `:` when reading `Configuration["Cosmos:Database"]`
    - Updated 7 hierarchical settings: Cosmos (4), Acs (2), Testing (1)
  - **Validation**: All functionality tested and working on Linux platform
  - **Documentation**: Created comprehensive migration guide in `docs/deployment/windows-to-linux-migration.md`
  - **Benefits**: Cost optimization, better container support, improved performance

### Added
- **Cosmos DB Continuous Backup Policy**: Production data protection with point-in-time restore (#104)
  - **Production**: Continuous backup with 30-day retention (Continuous30Days tier)
    - Automatic backups every 100 seconds
    - Point-in-time restore to any second within last 30 days
    - Near-zero RPO (Recovery Point Objective)
    - Cost: ~20% additional RU/s charge (~$70/month for 4000 RU/s)
  - **Dev/Staging**: Periodic backup (cost optimization)
    - Backup interval: 4 hours
    - Retention: 8 hours
    - Local storage redundancy
    - No additional cost
  - **Benefits**: Accidental deletion protection, compliance, audit trail, disaster recovery
  - **Configuration**: Environment-specific backup policy in `cosmos-db.bicep` module
- **Comprehensive Dependency Logging**: Enhanced observability for all external dependencies
  - Cosmos DB initialization logging: Database/container details, connection success/failure with exceptions
  - Redis connection event logging: ConnectionFailed, ConnectionRestored, ErrorMessage, InternalError with endpoints and failure types
  - RedisOtpStore enhanced logging: All operations (GET, SET, INCR) with user context, error types, cooldown status
  - GlobalExceptionHandlerMiddleware: Centralized unhandled exception logging with full request context (method, path, user, IP, user-agent)
  - Always log Cosmos/Redis at Information level, with verbose Development settings for troubleshooting
- **Health Check Logging**: Complete visibility into health check results
  - ILogger integration in all health checks (CosmosHealthCheck, RedisHealthCheck)
  - ApplicationInsightsHealthCheckPublisher: Publishes health check results to Application Insights every 30 seconds
  - Logs show which service failed, why, and latency for all checks
  - Enables easy troubleshooting of dependency failures in Azure Monitor
- **Application Insights Development Mode**: AI logging enabled for Development environment
  - Previously only Production environment sent logs to Application Insights
  - Development now uses Debug log level with verbose dependency logging for easier debugging
  - Maintains Information level for Production with dependency-specific logging

### Fixed
- **VNet Routing Configuration** (#TBD)
  - Fixed App Service outbound traffic routing to use private endpoints instead of public internet
  - Changed from deprecated `siteConfig.vnetRouteAllEnabled` to `outboundVnetRouting.allTraffic = true` (API version 2024-11-01)
  - Required for Windows App Services to properly route traffic through VNet to private endpoints
  - App now successfully connects to Cosmos DB, Redis, and SignalR via private IP addresses
- **Cosmos DB Users Container Partition Key** (#TBD)
  - Fixed partition key mismatch: changed from `/id` to `/userName` to match application expectations
  - Resolves startup failures caused by partition key validation errors
- **Redis Private Endpoint DNS Resolution**: Fixed DNS record for Redis private endpoint
  - Connection string used public endpoint but app needed private endpoint access
  - DNS record updated to point to private IP address (10.50.8.38)
  - Redis health check now passes with ~1ms latency via private endpoint

### Added
- **Infrastructure as Code Implementation**: Complete Azure Bicep templates for automated infrastructure deployment (#84)
  - ⚠️ **WARNING**: Bicep templates **NOT TESTED YET** - pending validation in dev/staging/prod environments
  - **Deployment Strategy**: GitHub Actions-only workflow with environment variables
    - Manual workflow_dispatch trigger with environment selection (dev/staging/prod)
    - 7 required environment variables per environment (BICEP_BASE_NAME, BICEP_LOCATION, BICEP_SHORT_LOCATION, BICEP_VNET_ADDRESS_PREFIX, BICEP_APP_SERVICE_SUBNET_PREFIX, BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX, BICEP_ACS_DATA_LOCATION)
    - No parameter files used in deployment (samples provided for reference only)
    - What-if analysis before deployment for change preview
    - Manual approval gate for production deployments
    - Post-deployment validation (verifies 2 subnets in VNet)
    - Optional teardown action for environment cleanup
  - **Bicep Modules Created**:
    - `networking.bicep`: VNet with 2 subnets (/27 each) + NSGs for App Service integration and Private Endpoints
    - `monitoring.bicep`: Log Analytics Workspace (30d/90d/365d retention) + Application Insights (workspace-based)
    - `cosmos-db.bicep`: Cosmos DB NoSQL with 3 containers (messages, users, rooms), autoscale throughput (400/1000/4000 RU/s), single region with zone redundancy, private endpoint only
    - `redis.bicep`: Azure Managed Redis (Microsoft.Cache/redisEnterprise), Balanced_B1/B3/B5 SKUs, port 10000, private endpoint only
    - `signalr.bicep`: Azure SignalR Service Standard_S1 for all environments, network ACLs (dev: all traffic, staging/prod: ClientConnection only on public), private endpoint
    - `communication.bicep`: Azure Communication Services with Europe data location
    - `app-service.bicep`: App Service Plan (P0V4 PremiumV4 Windows for all environments) + Web App (.NET 9.0 runtime, Windows OS) with VNet integration and outbound traffic routing, dual access mode (public + private), connection strings configured by Bicep
    - `main.bicep`: Main orchestration template with symbolic references, deterministic static IP allocation for private endpoints, no tags
  - **Networking Architecture**:
    - VNet with /26 CIDR block
    - TWO dedicated /27 subnets per environment (App Service integration + Private Endpoints)
    - IP-based subnet naming (e.g., `10-0-0-0--27`, `10-0-0-32--27`)
    - Private endpoints with deterministic static IP allocation:
      - Cosmos DB: .36 (global) + .37 (regional)
      - Redis: .38
      - SignalR: .39
      - App Service: .40
    - Network Security Groups on both subnets
  - **Network Access Control**:
    - Cosmos DB: Private endpoint only (public access disabled)
    - Redis: Private endpoint only (public access disabled)
    - SignalR: Dual access with network ACLs
      - Dev: All traffic types on public endpoint
      - Staging/Prod: ClientConnection only on public endpoint
      - Private endpoint: All traffic types (all environments)
    - App Service: Dual access mode (public + private endpoints enabled)
  - **Resource Naming Convention** (issue #84):
    - Networking Resource Group: `rg-vnet-{baseName}-{env}-{shortLocation}`
    - Application Resource Group: `rg-{baseName}-{env}-{shortLocation}`
    - App Service: `app-{baseName}-{env}-{shortLocation}`
    - Cosmos DB: `cdb-{baseName}-{env}-{shortLocation}`
    - Redis: `redis-{baseName}-{env}-{shortLocation}`
    - SignalR: `sigr-{baseName}-{env}-{shortLocation}`
    - Private Endpoint: `pe-{resourcename}`
  - **Documentation Updates**:
    - ARCHITECTURE.md: Infrastructure section updated with GitHub Actions deployment strategy
    - BOOTSTRAP.md: Automatic database seeding strategy
    - .github/workflows/README.md: Infrastructure and CD workflow documentation
    - infra/bicep/README.md: Comprehensive Bicep templates documentation
- **Automatic Database Seeding**: Database initialization moved to main application startup
  - Created `DataSeederService` that runs on app startup
  - Seeds database only if empty (no rooms AND no users)
  - Seeds 3 rooms (general, ops, random) and 3 users (alice, bob, charlie)
  - Idempotent and production-safe
- **Unified CD Workflow**: Single continuous deployment pipeline for all environments (#95)
  - Environment promotion via git workflow:
    - Push to `main` → auto-deploy to **dev**
    - Tag `rc*` (e.g., rc1.0.0) → deploy to **staging** (optional approval)
    - Tag `v*.*.*` (e.g., v1.0.0) → deploy to **prod** (required approval) + create GitHub Release
  - Simplified configuration: same 7 BICEP_* variables for infrastructure and application deployment
  - App Service name auto-constructed: `app-{BICEP_BASE_NAME}-{environment}-{BICEP_SHORT_LOCATION}`
  - Consistent 30-day artifact retention across all environments

### Changed
- **Database Seeding Strategy**: Moved from standalone tool to automatic application startup
  - Seeding now happens automatically during first app startup if database is empty
  - Removed manual seeding step from infrastructure deployment workflow
  - More reliable and production-safe with idempotent checks
- **CD/CD Architecture**: Unified deployment pipeline for simplified workflow management
  - Replaced separate `cd-staging.yml` and `cd-production.yml` with single `cd.yml`
  - Environment selection based on git workflow (main branch, rc tags, version tags)
  - Removed `AZURE_WEBAPP_NAME` variable - App Service name auto-constructed from Bicep variables
  - Eliminated redundant connection string management - Bicep configures everything during infrastructure deployment
- **Azure Region**: Default location from `eastus` to `polandcentral` for all environments
- **Azure Communication Services**: Data location from `United States` to `Europe`
- **Redis Service**: Migrated from Azure Cache for Redis to Azure Managed Redis (Microsoft.Cache/redisEnterprise)
- **SignalR Service**: Always uses Standard_S1 tier for all environments
- **App Service**: Standardized on P0V4 PremiumV4 for all environments

### Removed
- **Chat.DataSeed Tool**: Removed standalone seeding tool (676 lines) - replaced with automatic seeding
- **Separate CD Workflows**: Deleted `cd-staging.yml` and `cd-production.yml` - unified into `cd.yml`
- **Deployment Scripts**: Deleted manual deployment shell scripts
- **Configuration Complexity**: Removed `AZURE_WEBAPP_NAME` environment variable requirement

### Testing Status
- ⚠️ **PENDING**: Infrastructure deployment to dev environment
- ⚠️ **PENDING**: Infrastructure deployment to staging environment
- ⚠️ **PENDING**: Infrastructure deployment to production environment
- ⚠️ **PENDING**: VNet validation (2 subnets verification)
- ⚠️ **PENDING**: Private endpoints connectivity testing
- ⚠️ **PENDING**: Database seeding verification
- ⚠️ **PENDING**: Application startup and connection string validation

### Migration Notes
- **Before Deployment**: Configure 6 environment variables in GitHub repository settings for each environment
- **Service Principal Setup**: Configure Azure federated credentials for dev/staging/prod environments
- **Breaking Change**: This release requires complete infrastructure re-deployment (not an update to existing resources)
- **Cost Impact**: 
  - Dev: ~$150-250/month (P0V4 + Balanced_B1 + Serverless Cosmos)
  - Staging: ~$400-600/month (P0V4 + Balanced_B3 + Serverless Cosmos)
  - Production: ~$1200-1800/month (P0V4 + Balanced_B5 + Standard Cosmos + geo-replication)

## [0.9.5] - 2025-11-05

### Changed
- **Dependencies Update**: Updated frontend dependencies for improved features and security
  - Bootstrap updated from 5.2.0 to 5.3.8 (latest stable version with bug fixes and enhancements)
  - @microsoft/signalr pinned to version 9.0.6 (previously "latest", now explicit for reproducible builds)

### Fixed
- **CI/CD Workflows**: Fixed deployment pipeline to ensure npm-generated assets are included (#90)
  - Removed `--no-build` flag from `dotnet publish` in all workflows (CI, CD staging, CD production)
  - Added verification step to validate Bootstrap and SignalR libraries in publish output
  - Prevents deployment of incomplete packages missing `wwwroot/lib/*` files
  - Ensures production deployments include updated frontend dependencies

### Documentation
- **Security Guides**: Updated security documentation to reflect v0.9.4 implementation status (#88)
  - Corrected GUIDE-Session-handling.md with completed v0.9.4 features (HSTS, CSP, rate limiting)
  - Added comprehensive pepper management documentation to GUIDE-OTP-hashing.md
  - Documented storage options (environment variable, Azure App Service, Azure Key Vault)
  - Added pepper generation, rotation strategies, and troubleshooting guides
  - Reorganized mitigation roadmap by priority (completed, P1, P2, future)

## [0.9.4] - 2025-11-04

### Added
- **Azure Connection Strings Migration**: Migrated connection strings from Application Settings to Connection Strings section (#83)
  - Azure CLI migration script (`scripts/migrate-connection-strings.azcli`) for automated migration
  - Supports 4 connection strings: Cosmos, Redis, ACS, SignalR
  - Backward-compatible configuration reading with fallback to Application Settings
  - Connection Strings section preferred (injected as `CUSTOMCONNSTR_{name}` environment variables)
  - Verification and validation steps in migration script
  - Follows Azure best practices for connection string management
  - Tested on dev and staging environments (chat-dev-plc, chat-dev-plc-staging)
- **HSTS Configuration**: Enhanced HTTP Strict Transport Security with production-ready settings (#64)
  - 1-year max-age (31536000 seconds) for extended protection
  - `Preload` directive enabled for HSTS preload list eligibility
  - `IncludeSubDomains` enabled to protect all subdomains
  - Only applied in Production environment (not in test mode)
  - Expected header: `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`
  - Mitigates first-visit vulnerability and extends protection window from 30 days to 1 year
- **Content Security Policy (CSP) Headers**: Comprehensive security headers middleware to protect against XSS and other attacks
  - Content-Security-Policy with strict directives (script-src, style-src, connect-src, etc.)
  - Nonce-based inline script security for Login page
  - X-Content-Type-Options: nosniff to prevent MIME type sniffing
  - X-Frame-Options: DENY to prevent clickjacking attacks
  - Referrer-Policy: strict-origin-when-cross-origin for controlled referrer information
  - WebSocket (wss:) and HTTPS connections explicitly allowed for SignalR compatibility
  - All security headers applied early in the request pipeline
- **OTP Attempt Rate Limiting**: Per-user failed verification attempt tracking to prevent brute-force attacks (#26)
  - Redis-backed counter (`otp_attempts:{user}`) with atomic INCR operations
  - Configurable threshold (default: 5 attempts) via `Otp__MaxAttempts` environment variable
  - Automatic expiry (5-minute TTL synchronized with OTP lifetime)
  - Counter increments only on failed verification, not on success
  - OpenTelemetry metric: `chat.otp.verifications.ratelimited` tracks blocked attempts
  - Structured logging with sanitized usernames and attempt progression
  - Defense-in-depth: endpoint rate limiting (20 req/5s) + per-user attempt limiting (5 attempts)
  - Safe fallback on Redis errors (fail-open for availability)
  - Generic 401 responses prevent username enumeration
  - Comprehensive integration tests (3 tests covering all scenarios)

## [0.9.3] - 2025-10-30

### Added
- **Login Page Language Picker**: Added language selection UI to login page for consistent UX across the application
  - Flag icon button in login card header showing current language
  - Language selection modal with all 9 supported languages (matching chat page implementation)
  - Inline JavaScript for language switching without external dependencies
  - Preserves ReturnUrl and query parameters when switching languages
  - Active language highlighting and hover effects in modal

### Fixed
- **Localization System**: Complete internationalization fixes for 9 supported languages (en, pl-PL, de-DE, cs-CZ, sk-SK, uk-UA, be-BY, lt-LT, ru-RU)
  - Fixed case sensitivity issues in client-side translation keys (changed from PascalCase to camelCase to match API JSON serialization)
  - Resolved login page translation failures where SelectUser and 8 other keys always showed English regardless of selected language
  - Fixed login page redirect issue by removing site.js interference and inlining i18n loader directly in Login.cshtml
  - Implemented language picker with flag icon in profile section, replacing old dropdown in upper-right corner
  - Added language selection modal with all 9 language options, native language names, and active language highlighting
  - Fixed "Who's Here" user counter display to use parameterized translation string with dynamic count formatting
  - Removed unused search functionality from users list (container, CSS, and API endpoint)
  - Translated message read receipts ("Delivered" and "Read by") for all 9 languages
  - Added translations for language picker UI: ChangeLanguage, SelectLanguage, Logout
  - Fixed parameterized string tests by restoring `{0}` placeholder in WhosHere translations and updating UI to format count dynamically

## [0.9.2] - 2025-10-24

### Fixed
- **Documentation**: Updated ARCHITECTURE.md to reflect correct data access implementation (Cosmos DB with custom repositories, not Entity Framework Core)

## [0.9.1] - 2025-10-24

### Fixed
- **Application Insights Logging**: Added Serilog.Sinks.ApplicationInsights package to enable Production logging visibility
  - Configured Serilog to write logs directly to Application Insights in Production environment
  - Resolves observability gap where Information-level logs were not visible in Azure Monitor
  - Enables monitoring of notification system metrics (email/SMS delivery counts, user activity)
  - Uses `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable
  - Maintains OTLP and Console sinks for other environments
- **Logging Level Configuration**: Changed Production logging level from Warning to Information to capture application logs
  - Updated `appsettings.Production.json` to include Information-level logs for Chat.Web namespace

## [0.9.0] - 2025-10-24

### Added
- **CI/CD Pipelines**: Complete GitHub Actions workflows for continuous integration and deployment
  - CI pipeline: Build and test on all branches and PRs
  - CD Staging: Auto-deploy to staging on push to main
  - CD Production: Deploy to production on version tags with manual approval
  - Azure federated identity (OIDC) for secure authentication without stored secrets
- **Comprehensive Test Suite**: 54 tests across unit, integration, and data seeding
  - Integration tests with in-memory stores for fast execution
  - Proper ACS configuration for test environments
- **OpenTelemetry Observability**: Full-stack observability with traces, metrics, and logs
  - Automatic instrumentation for ASP.NET Core, HttpClient, Runtime, and Redis
  - Custom domain metrics for chat events (messages, rooms, OTP, presence)
  - Exporter selection: Azure Monitor → OTLP → Console
  - Conditional Redis instrumentation (disabled in test mode)
- **OTP Authentication System**: Secure one-time password authentication
  - Argon2id hashing with per-code salt and environment-supplied pepper
  - Versioned hash format for future algorithm upgrades
  - Dual-channel delivery (email + SMS) via Azure Communication Services
  - TTL-based expiration and rate limiting
- **Multi-Room Real-Time Chat**: SignalR-based chat with multiple fixed rooms
  - Rooms: `general`, `ops`, `random`
  - Optimistic message sending with client correlation IDs
  - Message reconciliation after reconnection
  - Incremental pagination (newest first, load older on scroll)
- **Read Receipts & Unread Notifications**: Track message read status
  - Persisted `ReadBy` data for each message
  - Broadcast read status to all room members
  - Delayed unread notifications (email/SMS) after configurable delay (60s default)
- **Health Endpoints**: Multiple health check endpoints for monitoring
  - `/healthz`: Liveness probe
  - `/healthz/ready`: Readiness probe (checks Cosmos + Redis)
  - `/healthz/metrics`: Lightweight metrics snapshot
- **Frontend Features**:
  - Dedicated `/login` and `/chat` pages
  - Optimistic send with retry logic
  - Outbox queue for offline message buffering
  - Browser title blinking for new messages when tab is hidden
  - Avatar initials generation (client-side)
  - Proactive reconnection on visibility change and network recovery
- **Bootstrap Tooling**: On-demand database seeding via .NET tool
  - Located in `tools/Chat.DataSeed/`
  - Supports dry-run and environment selection
  - Seeds rooms and initial users (alice, bob, charlie)
- **Documentation**:
  - `docs/BOOTSTRAP.md`: Database seeding and initialization guide
  - `docs/VERSIONING.md`: Comprehensive versioning and release strategy
  - `docs/GUIDE-OTP-hashing.md`: OTP implementation details
  - `docs/GUIDE-Session-handling.md`: Session and authentication flow
  - `docs/GUIDE-Visibility.md`: UI visibility and presence tracking
  - Grafana dashboard configurations for development observability

### Security
- **OTP Hashing**: Argon2id-based hashing for OTP codes (replaces plaintext storage)
  - Memory cost: 64 MB
  - Time cost: 4 iterations
  - Parallelism: 4 threads
  - Migration path for legacy plaintext OTPs
- **Sanitized Logging**: Removed all PII (personally identifiable information) from logs
  - No usernames, email addresses, or phone numbers in logs
  - Aggregate metrics only for notification recipients
- **Log Forging Prevention**: Fixed CWE-117 vulnerability
  - Sanitized HTTP request method and path before logging
  - Prevents attackers from injecting newlines into logs
- **Azure OIDC Federation**: No secrets stored in GitHub Actions
  - Environment-based federated credentials for staging and production
  - Least privilege permissions on all workflows
- **URL Validation**: Server-side validation of redirect URLs
  - `Url.IsLocalUrl` checks to prevent open redirects
  - Auth controller validates and approves redirect targets

### Changed
- **Architecture Refactoring**: Seeding → Bootstrap
  - Removed automatic seeding on startup (`DataSeedHostedService` deleted)
  - Replaced with on-demand seeding tool (similar to EF migrations)
  - Updated terminology from "seeding" to "test fixture initialization"
- **Integration Test Fixes**: Fixed room authorization tests
  - Modified `InMemoryUsersRepository.GetByUserName()` to return null instead of auto-creating users
  - Fixed OTP test to use `ConfigureTestServices()` instead of `WithWebHostBuilder()`
- **Notification Behavior**: All room members receive notifications regardless of login status
  - Changed from using `room.Users` (presence tracking) to `FixedRooms` (membership)
  - Removed deduplication logic (each user gets individual email/SMS)
  - Ensures offline users are notified within 60 seconds of unread messages
- **Cosmos TTL Configuration**: Made messages TTL configurable and nullable
  - `Cosmos:MessagesTtlSeconds` supports null (disable), -1 (enabled but never expire), or >0 (expire after N seconds)
  - Reconciles container TTL at startup to match configuration
- **Frontend Routing**: Separated login and chat pages
  - `/login`: Dedicated OTP authentication page
  - `/chat`: Protected chat interface (requires authentication)
  - Logout redirects to `/login?ReturnUrl=/chat`
- **Platform Consistency**: CI builds run on `windows-latest` to match Azure Windows App Service
  - Eliminates path separator, line ending, and case sensitivity issues

### Fixed
- **Integration Tests**: All 54 tests now passing (was 18 passing, 1 failing)
  - Fixed auto-user-creation bypassing seeded data in authorization tests
  - Added proper ACS configuration for test environments
- **Azure Authentication**: CD pipelines now use correct secrets syntax
  - Changed from `${{ vars.AZURE_CLIENT_ID }}` to `${{ secrets.AZURE_CLIENT_ID }}`
  - Fixes "Login failed" error in Azure deployment step
- **JavaScript Hoisting Bug**: Fixed site.js container reading before attributes loaded
  - Resolved initialization race condition
- **Test Warnings**: Removed CS0436 duplicate compile include warnings
  - Cleaned up `Chat.Tests.csproj` to avoid type conflicts

### Removed
- `DataSeedHostedService.cs`: Automatic seeding on startup (replaced with on-demand tool)
- `SeedingTests.cs`: Tests for automatic seeding (no longer applicable)
- Placeholder `appsettings.json`: Removed to avoid committing secrets (use environment-specific files)

[0.9.4]: https://github.com/smereczynski/SignalR-Chat/compare/v0.9.3...v0.9.4
[0.9.3]: https://github.com/smereczynski/SignalR-Chat/compare/v0.9.2...v0.9.3
[0.9.2]: https://github.com/smereczynski/SignalR-Chat/compare/v0.9.0...v0.9.2
[0.9.0]: https://github.com/smereczynski/SignalR-Chat/releases/tag/v0.9.0
