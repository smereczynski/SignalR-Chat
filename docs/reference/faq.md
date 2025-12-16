# Frequently Asked Questions (FAQ)

Common questions about SignalR Chat development, deployment, and troubleshooting.

## Table of Contents

- [General](#general)
- [Development](#development)
- [Testing](#testing)
- [Azure & Deployment](#azure--deployment)
- [Authentication & Security](#authentication--security)
- [Performance & Scalability](#performance--scalability)
- [Troubleshooting](#troubleshooting)

---

## General

### What is SignalR Chat?

SignalR Chat is a production-ready, real-time chat application built with ASP.NET Core 9, SignalR, and Azure services. It demonstrates modern web development practices including:
- Real-time messaging with SignalR
- OTP-based authentication (no passwords)
- Azure cloud infrastructure (Cosmos DB, Redis, SignalR Service)
- OpenTelemetry observability
- Multi-language support (9 languages)

### Can I use this in production?

**Yes**, with Azure resources configured. The application is production-ready with:
- ✅ Security hardening (CSP, HSTS, rate limiting, Argon2id)
- ✅ Observability (OpenTelemetry, Application Insights, Grafana)
- ✅ Scalability (Azure SignalR Service, Redis, Cosmos DB)
- ✅ High availability (multi-instance, zone redundancy)
- ❌ In-memory mode is **development only** (data lost on restart)

### What features are included?

**Core features**:
- Real-time messaging in chat rooms
- Read receipts (delivered, read by username)
- Typing indicators (3-second timeout)
- Presence tracking (online/offline status)
- OTP authentication (email or console)
- Multi-language support (9 languages)
- Automatic reconnection (exponential backoff)

**What's NOT included** (by design):
- ❌ Direct messages (DMs) - only fixed rooms
- ❌ Message editing/deletion
- ❌ User registration - fixed users (alice, bob, charlie, dave, eve)
- ❌ File uploads or rich media
- ❌ User avatars or profiles

### What technologies are used?

**Backend**:
- ASP.NET Core 9.0
- SignalR (real-time communication)
- C# 13

**Frontend**:
- Razor Pages
- Vanilla JavaScript (ES6+) - no jQuery
- Bootstrap 5
- SignalR JavaScript client

**Data**:
- Azure Cosmos DB (NoSQL) - messages, rooms, users
- Azure Cache for Redis - OTP storage, rate limiting
- In-memory fallback for development

**Cloud**:
- Azure App Service (Linux)
- Azure SignalR Service
- Azure Communication Services (OTP email)
- Application Insights (telemetry)

**Observability**:
- OpenTelemetry (traces, metrics, logs)
- Application Insights
- Grafana dashboards

---

## Development

### Do I need Azure to develop locally?

**No!** SignalR Chat supports **in-memory mode** for local development without any Azure dependencies.

**To run in true in-memory mode**, use the `Testing__InMemory=true` environment variable:

```bash
Testing__InMemory=true dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

**In-memory mode includes**:
- ✅ All features work (messaging, read receipts, typing, presence)
- ✅ No Azure account needed
- ✅ No connection strings required
- ✅ OTP codes logged to terminal
- ❌ Data lost on restart (no persistence)
- ❌ Single instance only (can't test load balancing)

**⚠️ Important**: Without `Testing__InMemory=true`, the application will connect to Azure resources if:
- `.env.local` file exists with connection strings
- Environment variables are set for Azure services
- Connection strings are configured for Cosmos/Redis (via `.env.local` or Azure App Service)

See the canonical configuration reference: **[Configuration Guide](../getting-started/configuration.md)**.

See [Quickstart Guide](../getting-started/quickstart.md) for 5-minute setup.

### How do I switch between in-memory and Azure mode?

**In-memory mode** (requires explicit flag):
```bash
Testing__InMemory=true dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

**Azure mode** (requires `.env.local` with connection strings):
```bash
bash -lc "set -a; source .env.local; dotnet run --project ./src/Chat.Web --urls=https://localhost:5099"
```

Or use VS Code task: **"Run Chat (Azure local env)"**

**How to verify which mode you're in**:
- **In-memory**: No Azure connections in logs, terminal shows OTP codes
- **Azure**: Logs show `Connecting to...` for Cosmos DB, SignalR Service, Redis

### What's in `.env.local`?

See **[Configuration Guide](../getting-started/configuration.md)** for the up-to-date `.env.local` template and supported keys.
# Azure SignalR Service
SIGNALR_CONNECTION_STRING="Endpoint=https://...;AccessKey=...;Version=1.0;"

# Azure Communication Services (optional - for email OTP)
ACS_CONNECTION_STRING="endpoint=https://...;accesskey=..."
ACS_EMAIL_FROM="DoNotReply@yourdomain.azurecomm.net"

# Application Insights (optional - for telemetry)
APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=...;IngestionEndpoint=..."
```

See [Local Setup Guide](../development/local-setup.md#full-setup-azure-mode) for details.

### How do I add a new language?

1. **Duplicate `.resx` file**:
   ```bash
   cp src/Chat.Web/Resources/SharedResources.en.resx \
      src/Chat.Web/Resources/SharedResources.xx.resx
   ```

2. **Translate strings** in new `.resx` file

3. **Add locale JSON**:
   ```bash
   cp src/Chat.Web/wwwroot/locales/en.json \
      src/Chat.Web/wwwroot/locales/xx.json
   ```

4. **Translate JSON strings**

5. **Rebuild** (required for `.resx` changes):
   ```bash
   rm -rf src/Chat.Web/bin src/Chat.Web/obj
   dotnet build ./src/Chat.sln
   ```

### How do I add a new user?

**In-memory mode**: Edit `Startup.cs` → `SeedData()` method

**Azure mode**: Add user to Cosmos DB `users` container:
```json
{
  "id": "newuser",
  "displayName": "New User",
  "phoneNumber": "+1234567890"
}
```

### How do I add a new chat room?

**In-memory mode**: Edit `Startup.cs` → `SeedData()` method

**Azure mode**: Add room to Cosmos DB `rooms` container:
```json
{
  "id": "newroom",
  "name": "New Room",
  "description": "Room description"
}
```

---

## Testing

### What tests are included?

SignalR Chat includes **135+ unit tests** covering core business logic:

```bash
# Run all tests
dotnet test src/Chat.sln
# Output: 135/135 passed ✅
```

**Test coverage**:
- ✅ OTP hashing and validation (Argon2id)
- ✅ Log sanitization (CWE-117 prevention)
- ✅ URL validation (security)
- ✅ Configuration guards (startup validation)
- ✅ Localization (9 languages)
- ✅ Service utilities (presence, notifications)

**Future work**: Integration tests and end-to-end tests can be implemented when they become a priority. Currently, the focus is on maintaining comprehensive unit test coverage.

See [Testing Guide](../development/testing.md) for details.

### How do I debug a failing test?

**VS Code**:
1. Open test file
2. Click "Debug Test" above test method
3. Set breakpoints
4. Inspect variables in Debug Console

**Command line**:
```bash
# Run with detailed output
dotnet test --filter "FullyQualifiedName~MyTest" --logger "console;verbosity=detailed"
```

See [Testing Guide: Debugging Tests](../development/testing.md#debugging-tests).

### What's the test coverage?

**Current**: 135+ unit tests covering core business logic

**Target**: >80% coverage on unit tests for pure logic and services

**Generate coverage report**:
```bash
dotnet test src/Chat.sln /p:CollectCoverage=true
```

---

## Azure & Deployment

### What Azure resources are required?

**Required** (for full functionality):
- Azure Cosmos DB (NoSQL) - messages, rooms, users
- Azure Cache for Redis - OTP storage, rate limiting
- Azure SignalR Service - load-balanced real-time connections
- Azure App Service (Linux) - web hosting

**Optional**:
- Azure Communication Services - email OTP delivery
- Application Insights - telemetry and monitoring
- Azure Front Door - CDN and WAF (future)

See [Installation Guide](../getting-started/installation.md) for step-by-step setup.

### How much does Azure cost?

**Development** (single instance, no zone redundancy):
- Cosmos DB (Serverless): $0.25/million RUs (~$5-10/month)
- Redis (Basic C0): ~$15/month
- SignalR (Free tier): $0/month
- App Service (F1 Free or B1 Basic): $0-13/month
- **Total**: ~$20-40/month

**Production** (3 instances, zone redundant):
- Cosmos DB (Autoscale 1000-4000 RU/s): ~$60-200/month
- Redis (Standard C1): ~$75/month
- SignalR (Standard S1): ~$50/month
- App Service (P1v3): ~$100/month
- Application Insights: ~$20/month
- **Total**: ~$300-450/month

Costs vary by region and usage. See [Azure Pricing Calculator](https://azure.microsoft.com/pricing/calculator/).

### Can I deploy without Bicep/IaC?

**Yes**, but not recommended. You can manually create resources in Azure Portal, but:
- ❌ No version control (hard to track changes)
- ❌ No repeatability (manual errors)
- ❌ No disaster recovery (can't recreate infrastructure)
- ✅ **Bicep is declarative** - describes desired state, not steps

**Recommendation**: Use Bicep templates in `infra/bicep/`:
```bash
az deployment group create \
  --resource-group rg-signalrchat-dev-weu \
  --template-file infra/bicep/main.bicep \
  --parameters @infra/bicep/main.parameters.dev.bicepparam
```

See [Deployment Guide](../deployment/README.md).

### What environments are supported?

Three environments with different configurations:

| Environment | Instances | Zone Redundancy | Purpose |
|-------------|-----------|-----------------|---------|
| **dev** | 1 | No | Development, testing |
| **staging** | 2 | Yes | Pre-production validation |
| **prod** | 3 | Yes | Production traffic |

Each environment has separate:
- Resource group (`rg-signalrchat-{env}-weu`)
- App Service (`app-signalrchat-{env}-weu`)
- Cosmos DB account (`cosmos-signalrchat-{env}-weu`)
- Redis cache (`redis-signalrchat-{env}-weu`)

### Can I deploy to Windows App Service?

**No**, only Linux is supported. Reasons:
- ✅ Better performance (Linux containers)
- ✅ Lower cost (Linux is cheaper)
- ✅ Docker support (future containerization)
- ✅ Modern .NET best practice

**Configuration note**: App settings use `__` (double underscore) on Linux:
```bicep
{
  name: 'Cosmos__Database'  // Not Cosmos:Database
  value: 'chat'
}
```

ASP.NET Core automatically translates `__` → `:` when reading configuration.

---

## Authentication & Security

### How does OTP authentication work?

1. **User selects username** (alice, bob, charlie, dave, eve)
2. **System generates 6-digit OTP** (valid 5 minutes)
3. **OTP sent via**:
   - **In-memory mode**: Logged to terminal
   - **Azure mode**: Email via Azure Communication Services
4. **User enters OTP** within 5 minutes
5. **System verifies** using Argon2id hash
6. **Cookie issued** for session (secure, httpOnly, sameSite)

**Security features**:
- ✅ Argon2id hashing (memory-hard, GPU-resistant)
- ✅ Rate limiting (max 5 attempts per 15 minutes)
- ✅ Time-limited codes (5 minutes TTL)
- ✅ Secure cookies (httpOnly, secure, sameSite)
- ❌ No passwords (eliminates credential stuffing, phishing)

### Why no passwords?

**Benefits of OTP-only authentication**:
- ✅ No password storage (no password database breaches)
- ✅ No password reuse (users can't reuse passwords)
- ✅ No weak passwords (system generates strong codes)
- ✅ No password reset flows (code expires automatically)
- ✅ Simpler UX (no "forgot password" flow)

**Trade-offs**:
- ❌ Requires email/SMS delivery (depends on Azure Communication Services)
- ❌ Less familiar to users (most expect passwords)

### Does Entra ID (SSO) work for local development?

**Not without proper setup**. Entra ID authentication requires:

**Required for Entra ID to work**:
- ✅ HTTPS enabled (`https://localhost:5099`)
- ✅ Valid Entra ID app registration in Azure Portal
- ✅ Redirect URI configured: `https://localhost:5099/signin-oidc`
- ✅ `EntraId:ClientId` configured via environment variables / appsettings overrides
- ✅ `EntraId:ClientSecret` (for web app flow)
- ✅ Dev certificate trusted (`dotnet dev-certs https --trust`)

**Why these requirements?**:
- Microsoft Identity Platform **requires HTTPS** for redirect URIs (security requirement)
- Localhost HTTP (`http://localhost:5099`) will **fail** with redirect URI mismatch
- Empty/placeholder ClientId will cause authentication failures

**Recommended for local development**:
- ✅ Use **in-memory mode with OTP authentication** (no Entra ID setup needed)
- ✅ Use `Testing__InMemory=true dotnet run`
- ✅ Test Entra ID in Azure-deployed environments (dev, staging, prod)

See [Entra ID Multi-Tenant Setup Guide](../development/entra-id-multi-tenant-setup.md) for full configuration.

For the canonical list of keys and examples, see **[Configuration Guide](../getting-started/configuration.md)**.

### How do I add password authentication?

This would require significant changes (not currently supported):
1. Add password field to User model
2. Implement password hashing (Argon2id)
3. Add login endpoint with username/password
4. Update UI for password input
5. Add "forgot password" flow
6. Update tests

**Recommendation**: Use OTP authentication as designed, or consider **OAuth2/OpenID Connect** with Azure Entra ID for enterprise scenarios.

### What security headers are configured?

**Production headers** (see `SecurityHeadersMiddleware.cs`):

```
Content-Security-Policy: default-src 'self'; script-src 'self' 'nonce-{random}'; style-src 'self' 'unsafe-inline'
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
```

**Why these headers?**:
- **CSP**: Prevents XSS (cross-site scripting) attacks
- **HSTS**: Forces HTTPS (prevents downgrade attacks)
- **X-Content-Type-Options**: Prevents MIME sniffing
- **X-Frame-Options**: Prevents clickjacking
- **Referrer-Policy**: Limits referrer leakage

### How do I prevent log injection attacks?

**Always use `LogSanitizer.Sanitize()`** before logging user input:

```csharp
// BAD ❌ - Vulnerable to log injection
_logger.LogWarning("Invalid input: {Input}", userInput);

// GOOD ✅ - Sanitized
_logger.LogWarning("Invalid input: {Input}", LogSanitizer.Sanitize(userInput));
```

**Why?** User input may contain newlines (`\n`, `\r`) that can forge log entries:

**Attack example**:
```
Input: "test\nERROR: Fake security breach!"
Log output:
  INFO: User input: test
  ERROR: Fake security breach!  ← Forged entry!
```

**After sanitization**:
```
Log output:
  INFO: User input: testERROR: Fake security breach!
```

See [.github/copilot-instructions.md](../../.github/copilot-instructions.md#log-sanitization) for full guidelines.

---

## Performance & Scalability

### How many users can SignalR Chat handle?

**Depends on infrastructure**:

**In-memory mode** (single instance):
- ~100-500 concurrent users
- Limited by single server resources
- No load balancing

**Azure mode** (with SignalR Service):
- Standard S1: Up to **1,000 concurrent connections** (100,000 messages/day)
- Premium P1: Up to **100,000 concurrent connections** (10M messages/day)
- Can scale horizontally with multiple App Service instances

**Bottlenecks**:
- Cosmos DB throughput (RU/s)
- Redis connection pool
- SignalR Service tier
- App Service CPU/memory

### How do I scale horizontally?

**With Azure SignalR Service**:
1. Increase App Service instance count (2-10 instances)
2. Azure SignalR Service handles connection distribution
3. Redis/Cosmos DB handle shared state
4. Load balancer distributes HTTP traffic

**Configuration**:
```bicep
resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  properties: {
    siteConfig: {
      numberOfWorkers: 3  // 3 instances
    }
  }
}
```

**Without Azure SignalR Service**:
- ❌ Doesn't work - SignalR requires sticky sessions
- ❌ WebSocket connections pinned to single server

### How do I optimize Cosmos DB costs?

**Tips**:
1. **Use serverless** for dev/staging (pay per request)
2. **Use autoscale** for prod (scale based on load)
3. **Optimize partition keys** (avoid hot partitions)
4. **Index only needed fields** (reduce RU consumption)
5. **Use TTL** to auto-delete old messages (reduce storage)

**Example**: Auto-delete messages older than 30 days:
```json
{
  "id": "message123",
  "content": "Hello",
  "ttl": 2592000  // 30 days in seconds
}
```

### How do I monitor performance?

**Built-in observability**:
- ✅ OpenTelemetry traces (request flows)
- ✅ Application Insights metrics (response times, failures)
- ✅ Grafana dashboards (custom visualizations)
- ✅ Health endpoints (`/health`, `/healthz`)

**Key metrics to monitor**:
- SignalR connection count
- Message send rate (messages/second)
- Cosmos DB RU consumption
- Redis connection pool usage
- App Service CPU/memory

See [Operations: Monitoring](../operations/monitoring.md) for details.

---

## Troubleshooting

### Port 5099 is already in use

**Solution**: Change port:
```bash
dotnet run --project ./src/Chat.Web --urls=http://localhost:5100
```

### OTP code not showing in terminal

**In-memory mode**: Check terminal where you ran `dotnet run`

**Azure mode**: Check email (Azure Communication Services) or enable terminal fallback

### SignalR connection fails

**Symptoms**: Browser console shows:
```
Failed to start the connection: Error: Unauthorized
```

**Possible causes**:
1. **Not authenticated** - Login first
2. **Cookie expired** - Login again
3. **CORS issue** - Check browser console for CORS errors
4. **Azure SignalR Service down** - Check Azure portal

**Debug steps**:
```bash
# Check health endpoint
curl http://localhost:5099/health

# Check SignalR negotiate
curl -i http://localhost:5099/chathub/negotiate
```

### Build fails after translation changes

**Solution**: Clean and rebuild:
```bash
rm -rf src/Chat.Web/bin src/Chat.Web/obj
dotnet build ./src/Chat.sln
```

**Why?** `.resx` files are compiled into satellite assemblies in `bin/` directory.

### Can't connect to Cosmos DB

**Symptoms**:
```
AggregateException: Unable to connect to DocumentDB
```

**Possible causes**:
1. **Wrong connection string** - Check `.env.local`
2. **Firewall rules** - Allow your IP in Cosmos DB firewall
3. **Network issue** - Check internet connection
4. **Cosmos DB account down** - Check Azure portal

**Debug steps**:
```bash
# Test connection
curl https://YOUR_ACCOUNT.documents.azure.com/

# Check firewall rules in Azure portal
# Cosmos DB account → Networking → Firewall and virtual networks
```

### Redis connection timeouts

**Symptoms**:
```
StackExchange.Redis.RedisTimeoutException: Timeout performing GET
```

**Possible causes**:
1. **Connection string wrong** - Check `.env.local`
2. **Redis overloaded** - Scale up Redis tier
3. **Network latency** - Use Redis in same region
4. **Connection pool exhausted** - Increase pool size

**Solution**: Adjust Redis configuration:
```json
{
  "Redis": {
    "ConnectionString": "...,connectTimeout=10000,syncTimeout=5000"
  }
}
```

### Tests pass locally, fail in CI

**Possible causes**:
1. **Timing issues** - CI environment may be slower
2. **Flaky tests** - Race conditions or shared state

**Solution**: Run tests multiple times locally to identify flaky tests:
```bash
for i in {1..10}; do dotnet test src/Chat.sln; done
```

### Application logs not showing in Azure

**Possible causes**:
1. **Application Insights not configured** - Check connection string
2. **Logging level too high** - Set to `Information` or `Debug`
3. **Telemetry disabled** - Check your environment configuration (see configuration guide)

**Solution**: Verify Application Insights:
```bash
# Check logs in Azure portal
# App Service → Monitoring → Log stream

# Check Application Insights
# Application Insights → Investigate → Logs (Kusto)
```

---

## Need More Help?

- **Documentation**: [docs/README.md](../README.md)
- **GitHub Issues**: [Open an issue](https://github.com/smereczynski/SignalR-Chat/issues)
- **GitHub Discussions**: [Ask a question](https://github.com/smereczynski/SignalR-Chat/discussions)
- **Contributing**: [CONTRIBUTING.md](../../CONTRIBUTING.md)

---

**Still have questions?** Open a [GitHub Discussion](https://github.com/smereczynski/SignalR-Chat/discussions) - we're here to help! 🎉
