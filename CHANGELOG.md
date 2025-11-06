# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Infrastructure as Code Implementation**: Complete Azure Bicep templates for automated infrastructure deployment (#84)
  - ⚠️ **WARNING**: Bicep templates and GitHub Actions workflow **NOT TESTED YET** - pending validation in dev/staging/prod environments
  - **Deployment Strategy**: GitHub Actions-only workflow (no manual scripts) with environment variables
    - Manual workflow_dispatch trigger with environment selection (dev/staging/prod)
    - 6 required environment variables per environment (BICEP_BASE_NAME, BICEP_LOCATION, BICEP_VNET_ADDRESS_PREFIX, BICEP_APP_SERVICE_SUBNET_PREFIX, BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX, BICEP_ACS_DATA_LOCATION)
    - No parameter files used in deployment (samples provided for reference only)
    - What-if analysis before deployment for change preview
    - Manual approval gate for production deployments
    - Automatic database seeding after infrastructure deployment
    - Post-deployment validation (verifies 2 subnets in VNet)
    - Optional teardown action for environment cleanup
  - **Bicep Modules Created**:
    - `networking.bicep`: VNet with 2 subnets (/27 each) + NSGs for App Service integration and Private Endpoints
    - `monitoring.bicep`: Log Analytics Workspace (30d/90d/365d retention) + Application Insights (workspace-based)
    - `cosmos-db.bicep`: Cosmos DB NoSQL with 3 containers (messages, users, rooms), serverless for dev/staging, geo-replication for prod, private endpoint
    - `redis.bicep`: **Azure Managed Redis** (Microsoft.Cache/redisEnterprise, not Azure Cache for Redis), Balanced_B1/B3/B5 SKUs, port 10000, private endpoint
    - `signalr.bicep`: Azure SignalR Service **always Standard_S1** (no Free tier for any environment), private endpoint
    - `communication.bicep`: Azure Communication Services with Europe data location
    - `app-service.bicep`: App Service Plan (P0V4 PremiumV4 for all environments) + Web App with VNet integration, all connection strings configured
    - `main.bicep`: Main orchestration template with symbolic references, no tags, no default parameter values
  - **Networking Architecture**:
    - VNet with /26 CIDR block
    - TWO dedicated /27 subnets per environment:
      - Subnet 1 (10.x.0.0/27): App Service VNet integration
      - Subnet 2 (10.x.0.32/27): Private Endpoints for data services
    - IP-based subnet naming (e.g., `10-0-0-0--27`, `10-0-0-32--27`)
    - Private endpoints for Cosmos DB (Sql), Redis (redisEnterprise), SignalR (signalr)
    - Network Security Groups on both subnets
  - **Resource Naming Convention** (issue #84):
    - Resource Group: `rg-{codename}-{env}-{location}`
    - App Service Plan: `serverfarm-{codename}-{env}-{location}`
    - App Service: `{codename}-{env}-{location}`
    - Cosmos DB: `cdb-{codename}-{env}-{location}`
    - Redis: `redis-{codename}-{env}-{location}`
    - SignalR: `sigr-{codename}-{env}-{location}`
    - Azure Communication Services: `acs-{codename}-{env}`
    - Application Insights: `ai-{codename}-{env}-{location}`
    - Log Analytics: `law-{codename}-{env}-{location}`
    - Private Endpoint: `pe-{resourcename}`
    - Private Endpoint NIC: `nic-pe-{resourcename}`
  - **SKU Configuration**:
    - App Service: P0V4 PremiumV4 for all environments (zone redundancy disabled for dev)
    - Azure Managed Redis: Balanced_B1 (dev), Balanced_B3 (staging), Balanced_B5 (prod)
    - SignalR Service: Standard_S1 with 1/1/5 units (dev/staging/prod) - **NO Free tier**
    - Cosmos DB: Serverless with zone redundancy for dev/staging, Standard with geo-replication for prod
  - **Configuration Management**:
    - All tags removed from Bicep templates
    - All default parameter values removed (environment variables only)
    - infra/bicep/main.json added to .gitignore
  - **GitHub Actions Workflow** (`.github/workflows/deploy-infrastructure.yml`):
    - Azure OIDC authentication (federated credentials)
    - Environment-based deployment (dev/staging/prod)
    - Deployment time: ~20-35 minutes
    - Automatic resource group creation
    - Connection strings output for application configuration
  - **Documentation Updates**:
    - ARCHITECTURE.md: Infrastructure section updated with GitHub Actions deployment strategy
    - BOOTSTRAP.md: Phase 1 replaced with GitHub Actions workflow instructions
    - README.md: Quick start updated with GitHub Actions workflow
    - .github/workflows/README.md: Infrastructure deployment workflow documentation
    - infra/bicep/README.md: Comprehensive Bicep templates documentation

### Changed
- **Azure Region**: Default location changed from `eastus` to `polandcentral` for all environments
- **Azure Communication Services**: Data location changed from `United States` to `Europe`
- **Redis Service**: Migrated from Azure Cache for Redis to **Azure Managed Redis** (Microsoft.Cache/redisEnterprise)
  - Connection port changed from 6379 to 10000
  - Two-tier resource structure (redisEnterprise + child databases)
  - Enterprise-grade clustering and encryption
- **SignalR Service**: Always uses Standard_S1 tier (removed Free_F1 option for dev environment)
- **App Service**: Standardized on P0V4 PremiumV4 for all environments (removed B1 for dev)
- **Deployment Method**: Shell scripts removed, **GitHub Actions-only** deployment strategy

### Removed
- **Deployment Scripts**: Deleted all manual deployment shell scripts (deploy.sh, validate.sh, teardown.sh)
- **Configuration**: Removed tags from all Bicep templates
- **Configuration**: Removed default parameter values from main.bicep
- **Legacy Scripts**: Removed `scripts/migrate-connection-strings.azcli` (superseded by Bicep)

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
