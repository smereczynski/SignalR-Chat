# Windows to Linux App Service Migration Guide

This document outlines all required changes to migrate the SignalR Chat application from Windows App Service to Linux App Service.

## Executive Summary

**Current State**: App Service Plan running on Windows with .NET 10.0
**Target State**: App Service Plan running on Linux with .NET 10.0 runtime
**Impact Level**: Medium - Requires Bicep changes, app settings cleanup, and validation
**Estimated Effort**: 2-4 hours (infrastructure changes + testing)

---

## 🔍 Key Differences: Windows vs Linux App Service

### Runtime Configuration

| Aspect | Windows | Linux |
|--------|---------|-------|
| **Runtime Stack** | `netFrameworkVersion: 'v10.0'` | `linuxFxVersion: 'DOTNETCORE\|10.0'` |
| **Plan Kind** | `kind: 'windows'` | `kind: 'linux'` |
| **Reserved Property** | Not required | `reserved: true` (required) |
| **App Service Kind** | `kind: 'app'` | `kind: 'app,linux'` |

### Application Insights Extensions

**Windows-Specific Settings** (must be removed for Linux):
```bicep
// ❌ These are Windows-only and must be removed:
ApplicationInsightsAgent_EXTENSION_VERSION: '~2'
APPINSIGHTS_PROFILERFEATURE_VERSION: '1.0.0'
APPINSIGHTS_SNAPSHOTFEATURE_VERSION: '1.0.0'
DiagnosticServices_EXTENSION_VERSION: '~3'
InstrumentationEngine_EXTENSION_VERSION: '~1'
SnapshotDebugger_EXTENSION_VERSION: '~2'
XDT_MicrosoftApplicationInsights_BaseExtensions: 'disabled'
XDT_MicrosoftApplicationInsights_Mode: 'recommended'
XDT_MicrosoftApplicationInsights_Java: '1'
```

**Linux-Compatible Settings** (keep these):
```bicep
// ✅ These work on both Windows and Linux:
APPLICATIONINSIGHTS_CONNECTION_STRING: '<connection-string>'
APPINSIGHTS_INSTRUMENTATIONKEY: '<instrumentation-key>'
```

**Why?** Windows uses IIS-based extensions that aren't available on Linux. Linux App Services use the SDK directly without agent extensions.

### VNet Integration & Outbound Routing

| Feature | Windows | Linux |
|---------|---------|-------|
| **VNet Integration** | `virtualNetworkSubnetId` | `virtualNetworkSubnetId` |
| **Outbound Routing** | `outboundVnetRouting.allTraffic: true` | `vnetRouteAllEnabled: true` |
| **Private Endpoints** | ✅ Supported | ✅ Supported |
| **Custom DNS** | ✅ Supported | ✅ Supported |

**Critical**: Linux uses the older `vnetRouteAllEnabled` property instead of `outboundVnetRouting` object.

### Health Checks & WebSockets

| Setting | Windows | Linux | Notes |
|---------|---------|-------|-------|
| `healthCheckPath` | ✅ Supported | ✅ Supported | Same path (`/healthz`) |
| `webSocketsEnabled` | ✅ Supported | ✅ Supported | Required for SignalR |
| `WEBSITE_HEALTHCHECK_MAXPINGFAILURES` | ✅ Supported | ✅ Supported | Same value (10) |
| `WEBSITE_HTTPLOGGING_RETENTION_DAYS` | ✅ Supported | ✅ Supported | Same value (7) |

### Connection Strings

**No changes required** - Both Windows and Linux support:
- `CUSTOMCONNSTR_Cosmos`
- `CUSTOMCONNSTR_Redis`
- `CUSTOMCONNSTR_SignalR`
- `CUSTOMCONNSTR_ACS`

Format remains identical.

### Configuration Hierarchy Notation

**CRITICAL: Linux requires `__` (double underscore) instead of `:` (colon) for environment variables!**

| Configuration Path | Windows | Linux | Azure App Service (Both) |
|-------------------|---------|-------|--------------------------|
| **appsettings.{Environment}.json** | `Cosmos:Database` | `Cosmos:Database` | `Cosmos:Database` |
| **Environment Variables** | `Cosmos:Database` ✅ | `Cosmos__Database` ⚠️ | `Cosmos:Database` ✅ |
| **App Settings UI** | `Cosmos:Database` | `Cosmos:Database` | `Cosmos:Database` |

**Why?** 
- Linux shells treat `:` as special characters (PATH separator)
- ASP.NET Core configuration on Linux translates `__` → `:` automatically
- Azure App Service handles this translation for you in the portal UI
- **Your Bicep uses `:` notation which is correct** - Azure translates it internally

**Impact**: ✅ **No changes needed** - Your Bicep templates use Azure App Service settings (not raw environment variables), so Azure handles the translation automatically.

---

## � Configuration Notation: `:` vs `__` (IMPORTANT!)

### The Issue

Linux shells treat `:` (colon) as a special character (PATH separator), so environment variables with colons don't work correctly:

```bash
# Linux shell - WRONG! 
export Cosmos:Database=chat  # Shell interprets : incorrectly

# Linux shell - CORRECT
export Cosmos__Database=chat  # Works correctly
```

ASP.NET Core's configuration system automatically translates `__` → `:` when reading configuration.

### How Your Code Handles This

Your codebase is **already Linux-compatible** because it uses both notations correctly:

#### ✅ Configuration API (uses `:` - works everywhere)

```csharp
// These work on both Windows and Linux:
Configuration["Cosmos:Database"]
Configuration["Testing:InMemory"]
Configuration["Otp:Pepper"]
Configuration.GetSection("Cosmos")
Configuration.GetConnectionString("Redis")
```

ASP.NET Core configuration provider reads from multiple sources and normalizes to `:` notation.

#### ✅ Direct Environment Variables (uses `__` - Linux-safe)

```csharp
// This correctly uses __ for direct env var access:
Environment.GetEnvironmentVariable("Otp__Pepper")  // Line 191, Startup.cs
```

This is the **only** place in your code that directly reads environment variables, and it correctly uses `__`.

### Azure App Service Behavior

**In Azure Portal → App Settings**, always use `:` (colon):

```bicep
appSettings: [
  {
    name: 'Cosmos:Database'        // ✅ Correct for Azure
    value: 'chat'
  }
  {
    name: 'Testing:InMemory'       // ✅ Correct for Azure
    value: 'false'
  }
]
```

**What Azure does internally**:
- **Windows**: Sets `Cosmos:Database=chat` directly
- **Linux**: Translates to `Cosmos__Database=chat` for the container
- **Your code**: Reads as `Configuration["Cosmos:Database"]` on both

### Local Development

#### Windows (`.env.local`)
```bash
# Both notations work on Windows:
Cosmos:Database=chat         # ✅ Works
Cosmos__Database=chat        # ✅ Also works
```

#### Linux/Mac (`.env.local`)
```bash
# Only double underscore works:
Cosmos__Database=chat              # ✅ Correct
Cosmos__MessagesContainer=messages # ✅ Correct
Otp__Pepper=YOUR_BASE64_PEPPER     # ✅ Correct

# This will NOT work:
Cosmos:Database=chat               # ❌ Shell error
```

### Summary Table

| Context | Notation | Example | Works On |
|---------|----------|---------|----------|
| **Bicep/Azure Portal** | `:` | `Cosmos:Database` | Both |
| **C# Configuration API** | `:` | `Configuration["Cosmos:Database"]` | Both |
| **C# Environment.GetEnvironmentVariable** | `__` | `Environment.GetEnvironmentVariable("Otp__Pepper")` | Both (required for Linux) |
| **Windows .env file** | `:` or `__` | Both work | Windows |
| **Linux .env file** | `__` only | Must use `__` | Linux/Mac |

### Action Required for Migration

**None!** Your Bicep templates and C# code already use the correct notation. Keep using `:` in Bicep - Azure handles the translation automatically.

---

## �📝 Required Bicep Changes

### 1. Update App Service Plan (`modules/app-service.bicep`)

```bicep
// Before (Windows):
resource appServicePlan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'serverfarm-${appName}'
  location: location
  sku: {
    name: skuConfig.name
    tier: skuConfig.tier
    capacity: skuConfig.capacity
  }
  kind: 'windows'  // ❌ Change this
  properties: {
    zoneRedundant: skuConfig.zoneRedundant
  }
}

// After (Linux):
resource appServicePlan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'serverfarm-${appName}'
  location: location
  sku: {
    name: skuConfig.name
    tier: skuConfig.tier
    capacity: skuConfig.capacity
  }
  kind: 'linux'  // ✅ Changed
  properties: {
    zoneRedundant: skuConfig.zoneRedundant
    reserved: true  // ✅ Required for Linux
  }
}
```

### 2. Update Web App Configuration (`modules/app-service.bicep`)

```bicep
// Before (Windows):
resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: appName
  location: location
  kind: 'app'  // ❌ Change this
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: vnetIntegrationSubnetId
    outboundVnetRouting: {  // ❌ Change this
      allTraffic: true
    }
    siteConfig: {
      netFrameworkVersion: 'v10.0'  // ❌ Remove this
      alwaysOn: true
      // ... rest of config
    }
  }
}

// After (Linux):
resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: appName
  location: location
  kind: 'app,linux'  // ✅ Changed
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: vnetIntegrationSubnetId
    vnetRouteAllEnabled: true  // ✅ Changed (Linux property)
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'  // ✅ Added (Linux runtime)
      alwaysOn: true
      // ... rest of config
    }
  }
}
```

### 3. Remove Windows-Only App Settings

```bicep
siteConfig: {
  linuxFxVersion: 'DOTNETCORE|10.0'
  alwaysOn: true
  http20Enabled: true
  minTlsVersion: '1.2'
  scmMinTlsVersion: '1.2'
  ftpsState: 'Disabled'
  healthCheckPath: '/healthz'
  webSocketsEnabled: true
  use32BitWorkerProcess: false
  loadBalancing: 'LeastRequests'
  minimumElasticInstanceCount: 1
  appSettings: [
    {
      name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
      value: appInsightsConnectionString
    }
    {
      name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
      value: appInsightsInstrumentationKey
    }
    // ❌ REMOVE ALL OF THESE (Windows-only):
    // - ApplicationInsightsAgent_EXTENSION_VERSION
    // - APPINSIGHTS_PROFILERFEATURE_VERSION
    // - APPINSIGHTS_SNAPSHOTFEATURE_VERSION
    // - DiagnosticServices_EXTENSION_VERSION
    // - InstrumentationEngine_EXTENSION_VERSION
    // - SnapshotDebugger_EXTENSION_VERSION
    // - XDT_MicrosoftApplicationInsights_BaseExtensions
    // - XDT_MicrosoftApplicationInsights_Mode
    // - XDT_MicrosoftApplicationInsights_Java
    
    // ✅ KEEP THESE (work on both):
    {
      name: 'ASPNETCORE_ENVIRONMENT'
      value: environment == 'prod' ? 'Production' : (environment == 'staging' ? 'Production' : 'Development')
    }
    {
      name: 'Cosmos:Database'  // ✅ Colon notation is CORRECT for Azure App Settings
      value: 'chat'
    }
    {
      name: 'Cosmos:MessagesContainer'
      value: 'messages'
    }
    {
      name: 'Cosmos:RoomsContainer'
      value: 'rooms'
    }
    {
      name: 'Cosmos:UsersContainer'
      value: 'users'
    }
    {
      name: 'Acs:EmailFrom'
      value: 'doNotReply@${split(split(acsConnectionString, 'endpoint=https://')[1], '.')[0]}.azurecomm.net'
    }
    {
      name: 'Acs:SmsFrom'
      value: 'TRANSLATOR'
    }
    {
      name: 'WEBSITE_HEALTHCHECK_MAXPINGFAILURES'
      value: '10'
    }
    {
      name: 'WEBSITE_HTTPLOGGING_RETENTION_DAYS'
      value: '7'
    }
    {
      name: 'Testing:InMemory'
      value: 'false'
    }
  ]
  connectionStrings: [
    // ✅ NO CHANGES NEEDED - same for both Windows and Linux
    {
      name: 'Cosmos'
      connectionString: cosmosConnectionString
      type: 'Custom'
    }
    {
      name: 'Redis'
      connectionString: redisConnectionString
      type: 'Custom'
    }
    {
      name: 'SignalR'
      connectionString: signalRConnectionString
      type: 'Custom'
    }
    {
      name: 'ACS'
      connectionString: acsConnectionString
      type: 'Custom'
    }
  ]
}
```

**Important**: Keep using `:` (colon) notation in Bicep `appSettings`! Azure App Service automatically translates this to `__` for Linux containers. Your application code reads it as `:` through ASP.NET Core configuration.

---

## ✅ Application Code Changes

**Good news**: No application code changes required! The ASP.NET Core 10 application is platform-agnostic.

### Configuration Notation: `:` vs `__`

Your code correctly uses **both** notations where appropriate:

**In C# Code** (uses `:` - works on both platforms):
```csharp
Configuration["Cosmos:Database"]           // ✅ Correct
Configuration["Testing:InMemory"]          // ✅ Correct
Configuration.GetSection("Otp")            // ✅ Correct
```

**In Environment Variables** (Linux requires `__`):
```csharp
Environment.GetEnvironmentVariable("Otp__Pepper")  // ✅ Correct for Linux!
```

**Your code is already Linux-compatible!** The only place using `__` is `Otp__Pepper` environment variable (line 191 in Startup.cs), which is correct for both Windows and Linux.

**Azure App Service Translation**:
- When you set `Cosmos:Database` in Azure Portal → App Settings
- Azure automatically translates it to `Cosmos__Database` for Linux containers
- Your C# code reads it as `Configuration["Cosmos:Database"]`
- ASP.NET Core configuration system handles the mapping

**Local Development** (`.env.local` file):
```bash
# Windows - both work:
Cosmos:Database=chat
Cosmos__Database=chat

# Linux/Mac - only double underscore works:
Cosmos__Database=chat  # ✅ Correct
Cosmos:Database=chat   # ❌ Shell interprets : as PATH separator
```

### Verified Compatible Components

- ✅ **ASP.NET Core 10**: Works identically on both Windows and Linux
- ✅ **SignalR**: Cross-platform (both in-process and Azure SignalR Service)
- ✅ **Cosmos DB SDK**: Platform-agnostic
- ✅ **StackExchange.Redis**: Works on both platforms
- ✅ **Azure Communication Services**: Platform-agnostic
- ✅ **OpenTelemetry**: Cross-platform
- ✅ **Serilog**: Works on both platforms
- ✅ **Argon2 Hashing** (Isopoh.Cryptography.Argon2): Pure .NET, cross-platform
- ✅ **Localization**: ASP.NET Core localization is platform-agnostic

### Dockerfile (Optional - Not Required)

Linux App Service can deploy directly from published output without a Dockerfile. However, if you want explicit control:

```dockerfile
# Optional: Custom Dockerfile for Linux App Service
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "Chat.Web.dll"]
```

**Note**: The default Linux App Service runtime already provides .NET 10 - no Dockerfile needed.

---

## 🔒 Security & Networking Considerations

### Private Endpoints
- ✅ **No changes required** - Linux supports private endpoints identically to Windows
- ✅ VNet integration works the same way (subnet delegation)
- ✅ `vnetRouteAllEnabled: true` on Linux = `outboundVnetRouting.allTraffic: true` on Windows

### TLS & HTTPS
- ✅ **No changes** - TLS 1.2 minimum works on both
- ✅ HTTPS-only enforcement identical
- ✅ Certificate handling identical (Azure-managed or custom)

### Managed Identity
- ✅ **Compatible** - System-assigned and user-assigned identities work on both
- Current infrastructure doesn't use managed identity (uses connection strings)
- Future enhancement: Consider enabling for Cosmos DB / Key Vault access

---

## 📊 Testing Checklist

### Pre-Deployment Validation

- [ ] **Bicep Validation**: Run `az deployment sub validate` with updated templates
- [ ] **What-If Preview**: Review all resource changes before deployment
- [ ] **Backup**: Document current configuration in Azure Portal

### Post-Deployment Testing

#### 1. Basic Connectivity
```bash
# Health check
curl https://<app-url>/healthz
# Expected: 200 OK

# Readiness check
curl https://<app-url>/healthz/ready
# Expected: 200 OK with Cosmos + Redis checks
```

#### 2. Application Insights
```bash
# Verify telemetry flowing
az monitor app-insights metrics show \
  --app <app-insights-name> \
  --resource-group <rg-name> \
  --metrics "requests/count"
```

#### 3. SignalR Connection
- [ ] Open chat UI in browser
- [ ] Verify WebSocket connection established (check browser DevTools Network tab)
- [ ] Send test message - should appear in real-time
- [ ] Check Azure SignalR Service metrics for active connections

#### 4. Private Endpoints
```bash
# From App Service Kudu console (https://<app-name>.scm.azurewebsites.net)
# Test Cosmos DB private endpoint
nslookup <cosmos-account>.documents.azure.com
# Expected: Resolves to 10.x.x.x (private IP)

# Test Redis private endpoint
nslookup <redis-name>.redis.cache.windows.net
# Expected: Resolves to 10.x.x.x (private IP)

# Test SignalR private endpoint
nslookup <signalr-name>.service.signalr.net
# Expected: Resolves to 10.x.x.x (private IP)
```

#### 5. OTP Authentication
- [ ] Navigate to `/login`
- [ ] Request OTP code (verify console output or email/SMS delivery)
- [ ] Verify code - should authenticate successfully
- [ ] Check Redis for OTP storage (`KEYS otp:*` in Redis Console)

#### 6. Localization
- [ ] Test language switcher on login page
- [ ] Verify translations load (check `/api/localization/strings` endpoint)
- [ ] Confirm culture-specific formatting (dates, times)

#### 7. Performance Baseline
```bash
# Run load test (Apache Bench example)
ab -n 1000 -c 10 https://<app-url>/healthz

# Compare response times:
# - Windows baseline: ~50-100ms (document current)
# - Linux target: Should be similar or faster
```

---

## 💰 Cost Impact

### Expected Changes
- **App Service Plan**: Same cost (P0V4 pricing identical for Windows and Linux)
- **No additional charges** for Linux runtime
- **Potential savings**: Linux can be more resource-efficient for .NET Core workloads

### SKU Pricing (for reference)
| SKU | Windows | Linux | Notes |
|-----|---------|-------|-------|
| **P0V4** | ~$100/month | ~$100/month | Same pricing |
| **P1V4** | ~$200/month | ~$200/month | Same pricing |

---

## 🚀 Deployment Strategy

### Recommended Approach: Blue-Green Deployment

1. **Phase 1: Deploy to Dev Environment**
   ```bash
   # Deploy Linux infrastructure to dev
   az deployment sub create \
     --location westeurope \
     --template-file infra/bicep/main.bicep \
     --parameters infra/bicep/main.parameters.dev.bicepparam
   
   # Test thoroughly (all checklist items)
   ```

2. **Phase 2: Deploy to Staging**
   ```bash
   # Deploy to staging after dev validation
   az deployment sub create \
     --location westeurope \
     --template-file infra/bicep/main.bicep \
     --parameters infra/bicep/main.parameters.staging.bicepparam
   
   # Run integration tests
   # Load testing
   ```

3. **Phase 3: Deploy to Production**
   ```bash
   # Production deployment (requires approval)
   # Use GitHub Actions workflow with manual approval gate
   ```

### Rollback Plan

If issues occur after Linux migration:

1. **Immediate Rollback** (< 1 hour):
   ```bash
   # Revert Bicep changes to Windows configuration
   git revert <commit-hash>
   
   # Redeploy Windows infrastructure
   az deployment sub create \
     --location westeurope \
     --template-file infra/bicep/main.bicep \
     --parameters infra/bicep/main.parameters.<env>.bicepparam
   ```

2. **Data Integrity**: No impact - Cosmos DB and Redis data remain unchanged

---

## 📚 References

### Official Documentation
- [Linux App Service Overview](https://learn.microsoft.com/azure/app-service/overview#app-service-on-linux)
- [.NET on Linux App Service](https://learn.microsoft.com/azure/app-service/quickstart-dotnetcore)
- [VNet Integration for Linux](https://learn.microsoft.com/azure/app-service/overview-vnet-integration)
- [Application Insights for .NET Core](https://learn.microsoft.com/azure/azure-monitor/app/asp-net-core)

### Internal Documentation
- [Current Infrastructure README](../../infra/bicep/README.md)
- [Architecture Overview](../architecture/overview.md)
- [Deployment Guide](./azure.md)

---

## 🎯 Summary of Changes

### Bicep Changes
| File | Lines to Change | Description |
|------|-----------------|-------------|
| `modules/app-service.bicep` | ~15 lines | App Service Plan kind, Web App kind, runtime config |
| `modules/app-service.bicep` | ~20 lines | Remove Windows-only App Insights settings |
| `modules/app-service.bicep` | ~3 lines | Update VNet routing property |

### Total Impact
- **Application Code**: 0 changes
- **Infrastructure**: ~40 lines in 1 file
- **Testing Time**: 2-3 hours
- **Risk Level**: Low (easily reversible)

### Benefits
- ✅ Platform consistency (Linux everywhere in infra)
- ✅ Better performance for .NET Core workloads
- ✅ Simplified App Insights configuration (SDK-only, no agent)
- ✅ Future-proof (Microsoft investing heavily in Linux for .NET)

---

## ❓ FAQ

**Q: Will this affect existing data?**
A: No - Cosmos DB, Redis, and all data services remain unchanged. Only the App Service compute layer changes.

**Q: Do we need to update CI/CD pipelines?**
A: No - GitHub Actions workflows deploy to App Service using the same method. The `az webapp deploy` command works identically for both Windows and Linux.

**Q: What about custom domain and SSL certificates?**
A: No changes required - custom domains, SSL certificates, and DNS remain identical.

**Q: Can we run Windows and Linux simultaneously?**
A: Yes, temporarily during migration. You could run dev on Linux while staging/prod remain on Windows. This enables gradual rollout.

**Q: What if we need Windows-specific features later?**
A: Easy to revert - just reverse the Bicep changes. However, ASP.NET Core 10 is fully cross-platform, so this is unlikely.

**Q: Do I need to change `Cosmos:Database` to `Cosmos__Database` in Bicep?**
A: **No!** Keep using `:` (colon) in Bicep and Azure Portal. Azure App Service automatically translates this for Linux containers. Your C# code continues to use `Configuration["Cosmos:Database"]`.

**Q: What about local development on Linux/Mac?**
A: Use `__` (double underscore) in `.env.local` files:
```bash
# .env.local (Linux/Mac)
Cosmos__Database=chat          # ✅ Correct
Cosmos__MessagesContainer=messages
Otp__Pepper=YOUR_BASE64_PEPPER
```

**Q: Why does the code use `Otp__Pepper` but other settings use `:`?**
A: `Otp__Pepper` is read directly from environment variables via `Environment.GetEnvironmentVariable()`, which requires `__` on Linux. Other settings use ASP.NET Core Configuration API (`Configuration["Cosmos:Database"]`), which handles the translation automatically.

---

## ✍️ Next Steps

1. [ ] Review this document with team
2. [ ] Update Bicep templates for dev environment
3. [ ] Test deployment to dev
4. [ ] Document any issues encountered
5. [ ] Update this guide based on learnings
6. [ ] Proceed to staging after successful dev validation
7. [ ] Schedule production migration

**Estimated Timeline**: 1-2 weeks (including validation periods between environments)
