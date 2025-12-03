# Translation Service Environment Configuration

This document explains how the Translation Service configuration is set up across different environments (local development, Azure App Service) and how environment variables map to application configuration.

## Configuration Architecture

The Translation Service uses different configuration approaches based on the deployment target:

- **Local Development**: Environment variables (`.env.local`) → `appsettings.Development.json`
- **Azure App Service**: Bicep-managed App Settings → `appsettings.Production.json`
- **Azure Infrastructure**: Managed Identity-based authentication (no subscription keys)

---

## Local Development Configuration

### `.env.local` Environment Variables

For local development, create a `.env.local` file (from `.env.local.example`) with the following Translation section:

```bash
# Azure AI Translator Service
# Configuration for real-time message translation (NMT or LLM-based)
# Leave Enabled=false or empty to disable translation functionality
Translation__Enabled=false
Translation__Endpoint=
Translation__SubscriptionKey=
Translation__Region=
Translation__ApiVersion=2025-10-01-preview
Translation__DeploymentName=
Translation__CacheTtlSeconds=3600
```

### Configuration Properties

| Environment Variable | Type | Description | Default | Required |
|---------------------|------|-------------|---------|----------|
| `Translation__Enabled` | bool | Enable/disable translation service | `false` | No |
| `Translation__Endpoint` | string | Azure Translator endpoint URL | - | Yes (if enabled) |
| `Translation__SubscriptionKey` | string | API subscription key (for local dev only) | - | Yes (if enabled) |
| `Translation__Region` | string | Azure region (e.g., `westeurope`) | - | Yes (if enabled) |
| `Translation__ApiVersion` | string | Translation API version | `2025-10-01-preview` | No |
| `Translation__DeploymentName` | string | LLM deployment name (e.g., `gpt-4o-mini`) | - | No |
| `Translation__CacheTtlSeconds` | int | Redis cache TTL in seconds | `3600` | No |

### Example Configuration (Local Development)

```bash
Translation__Enabled=true
Translation__Endpoint=https://my-translator-dev.cognitiveservices.azure.com
Translation__SubscriptionKey=your-subscription-key-here
Translation__Region=westeurope
Translation__ApiVersion=2025-10-01-preview
Translation__DeploymentName=gpt-4o-mini
Translation__CacheTtlSeconds=3600
```

**Security Note**: For local development with subscription keys:
- Never commit `.env.local` to Git (it's in `.gitignore`)
- Use a development/test resource with limited quotas
- Rotate keys regularly

---

## Azure App Service Configuration

### Infrastructure (Bicep)

The Translation Service is provisioned via the `translation.bicep` module and configured in `app-service.bicep`. The infrastructure creates an **Azure AI Services** (Microsoft Foundry) resource with **Managed Identity** authentication.

#### Bicep Parameters (in `main.bicep`)

```bicep
@description('Enable translation service')
param enableTranslation bool = false

@description('Translation provider: NMT (Neural Machine Translation), LLM-GPT4oMini, or LLM-GPT4o')
param translationProvider string = 'LLM-GPT4oMini'
```

#### App Service App Settings (Bicep → Environment Variables)

The `app-service.bicep` module creates the following App Settings:

| App Setting Name | Bicep Parameter | Description |
|------------------|-----------------|-------------|
| `Translation__Enabled` | `translationEnabled` | Enable/disable translation (`true`/`false`) |
| `Translation__ResourceId` | `translationResourceId` | Azure resource ID of AI Services account |
| `Translation__Endpoint` | `translationEndpoint` | Endpoint URL (e.g., `https://aif-signalrchat-dev-plc.cognitiveservices.azure.com`) |
| `Translation__Provider` | `translationProvider` | Provider type: `NMT`, `LLM-GPT4oMini`, or `LLM-GPT4o` |
| `Translation__ModelDeploymentName` | `translationModelDeploymentName` | Model deployment name (e.g., `gpt-4o-mini`) |

**Key Differences from Local Development**:
- ❌ **No `SubscriptionKey`**: Azure App Service uses **Managed Identity** to authenticate to AI Services
- ✅ **`ResourceId`**: Required for managed identity authentication
- ✅ **`Provider`**: Specifies translation method (NMT vs LLM)

### Managed Identity Authentication

Azure App Service is granted **Cognitive Services User** role on the AI Services account automatically by the Bicep deployment. This eliminates the need for subscription keys in production.

**Benefits**:
- No secret management (no keys to rotate)
- Automatic credential rotation
- Audit logs via Entra ID
- Role-based access control (RBAC)

### appsettings.Production.json

The production configuration file uses placeholders for environment-specific values:

```json
"Translation": {
  "Enabled": true,
  "Endpoint": "${TRANSLATION_ENDPOINT}",
  "SubscriptionKey": "${TRANSLATION_SUBSCRIPTION_KEY}",
  "Region": "${TRANSLATION_REGION}",
  "ApiVersion": "2025-10-01-preview",
  "DeploymentName": "${TRANSLATION_DEPLOYMENT_NAME}"
}
```

**Note**: In Azure App Service with Managed Identity, `SubscriptionKey` is **not set** and the SDK automatically uses managed identity credentials.

---

## Configuration Mapping Summary

### Local Development (`.env.local` → `appsettings.Development.json`)

ASP.NET Core configuration binding automatically maps environment variables with `__` (double underscore) to hierarchical configuration:

```
Translation__Enabled          → Translation:Enabled
Translation__Endpoint         → Translation:Endpoint
Translation__SubscriptionKey  → Translation:SubscriptionKey
Translation__Region           → Translation:Region
Translation__ApiVersion       → Translation:ApiVersion
Translation__DeploymentName   → Translation:DeploymentName
Translation__CacheTtlSeconds  → Translation:CacheTtlSeconds
```

### Azure App Service (Bicep → App Settings → `appsettings.Production.json`)

The Bicep deployment creates App Settings with `__` notation, which ASP.NET Core reads directly:

| Bicep Output | App Setting | Configuration Path |
|--------------|-------------|-------------------|
| `translation.outputs.resourceId` | `Translation__ResourceId` | `Translation:ResourceId` |
| `translation.outputs.endpoint` | `Translation__Endpoint` | `Translation:Endpoint` |
| `translationProvider` | `Translation__Provider` | `Translation:Provider` |
| `translation.outputs.modelDeploymentName` | `Translation__ModelDeploymentName` | `Translation:ModelDeploymentName` |
| (parameter) | `Translation__Enabled` | `Translation:Enabled` |

**Missing from appsettings.json** (needs to be added):
- `Translation:ResourceId` - Required for Managed Identity
- `Translation:Provider` - Required to distinguish NMT vs LLM

---

## Configuration Differences: Local vs Azure

| Property | Local Development | Azure App Service |
|----------|------------------|-------------------|
| **Authentication** | Subscription Key | Managed Identity |
| **ResourceId** | Not used | Required |
| **SubscriptionKey** | Required | Not used (ignored) |
| **Provider** | Not used | Required (`NMT`, `LLM-GPT4oMini`, `LLM-GPT4o`) |
| **Region** | Required | Derived from resource location |
| **DeploymentName** | Optional | Set by Bicep if LLM provider |

---

## Required Configuration Updates

### 1. Update `TranslationOptions.cs`

Add missing properties to match Bicep infrastructure:

```csharp
public class TranslationOptions
{
    public bool Enabled { get; set; } = true;
    
    // Azure AI Services resource
    public string? ResourceId { get; set; }          // NEW: For Managed Identity
    public string? Endpoint { get; set; }
    public string? SubscriptionKey { get; set; }     // Only for local dev
    
    // Configuration
    public string? Region { get; set; }
    public string ApiVersion { get; set; } = "2025-10-01-preview";
    
    // Provider and deployment
    public string? Provider { get; set; }             // NEW: NMT, LLM-GPT4oMini, LLM-GPT4o
    public string? DeploymentName { get; set; }       // Rename: ModelDeploymentName → DeploymentName
    
    // Caching
    public int CacheTtlSeconds { get; set; } = 3600;
}
```

### 2. Update `appsettings.Production.json`

Add missing properties:

```json
"Translation": {
  "Enabled": true,
  "ResourceId": "${TRANSLATION_RESOURCE_ID}",
  "Endpoint": "${TRANSLATION_ENDPOINT}",
  "Region": "${TRANSLATION_REGION}",
  "ApiVersion": "2025-10-01-preview",
  "Provider": "${TRANSLATION_PROVIDER}",
  "DeploymentName": "${TRANSLATION_MODEL_DEPLOYMENT_NAME}",
  "CacheTtlSeconds": 3600
}
```

**Note**: Remove or leave empty `SubscriptionKey` for production (Managed Identity doesn't need it).

### 3. Update `AzureTranslatorService.cs`

Detect authentication method based on configuration:

```csharp
public AzureTranslatorService(
    HttpClient httpClient,
    IOptions<TranslationOptions> options,
    ILogger<AzureTranslatorService> logger,
    IConnectionMultiplexer? redis = null)
{
    _httpClient = httpClient;
    _options = options.Value;
    _logger = logger;
    _redis = redis;

    // Configure authentication
    if (!string.IsNullOrWhiteSpace(_options.SubscriptionKey))
    {
        // Local development: Use subscription key
        _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);
        _logger.LogInformation("Using subscription key authentication for Translation Service");
    }
    else if (!string.IsNullOrWhiteSpace(_options.ResourceId))
    {
        // Azure: Use Managed Identity (Azure.Identity SDK)
        _logger.LogInformation("Using Managed Identity authentication for Translation Service (ResourceId: {ResourceId})", 
            LogSanitizer.Sanitize(_options.ResourceId));
        // TODO: Implement DefaultAzureCredential token provider
    }
    else
    {
        throw new InvalidOperationException("Translation Service requires either SubscriptionKey (local dev) or ResourceId (Azure with Managed Identity)");
    }
}
```

---

## Deployment Checklist

### Local Development
- [ ] Copy `.env.local.example` to `.env.local`
- [ ] Set `Translation__Enabled=true`
- [ ] Set `Translation__Endpoint` (Azure Translator resource URL)
- [ ] Set `Translation__SubscriptionKey` (from Azure Portal → Keys)
- [ ] Set `Translation__Region` (e.g., `westeurope`)
- [ ] Set `Translation__DeploymentName` (if using LLM, e.g., `gpt-4o-mini`)
- [ ] Run: `bash -lc "set -a; source .env.local; dotnet run --project ./src/Chat.Web"`

### Azure App Service (via Bicep)
- [ ] Set `enableTranslation=true` in Bicep parameters
- [ ] Set `translationProvider` (`NMT`, `LLM-GPT4oMini`, or `LLM-GPT4o`)
- [ ] Deploy infrastructure: `az deployment group create ...`
- [ ] Verify App Settings created in App Service (Azure Portal → Configuration)
- [ ] Verify Managed Identity assigned to App Service
- [ ] Verify RBAC role assignment (Cognitive Services User) on AI Services account
- [ ] Deploy application code
- [ ] Test translation functionality

---

## Troubleshooting

### "Translation Service requires either SubscriptionKey or ResourceId"
- **Local Dev**: Ensure `Translation__SubscriptionKey` is set in `.env.local`
- **Azure**: Ensure `Translation__ResourceId` App Setting exists (created by Bicep)

### "401 Unauthorized" on Translation API
- **Local Dev**: Verify subscription key is correct (check Azure Portal → Keys)
- **Azure**: Verify Managed Identity is enabled and has RBAC role (Cognitive Services User)

### "DeploymentName required for LLM provider"
- Ensure `Translation__DeploymentName` matches the deployed model name (e.g., `gpt-4o-mini`, `gpt-4o`)
- Verify model is deployed in Azure AI Services (Portal → Deployments)

### Translation not working but no errors
- Check `Translation__Enabled=true`
- Verify Redis connection if caching is expected
- Check Application Insights or logs for warnings

---

## References

- [Azure AI Translator REST API](https://learn.microsoft.com/azure/ai-services/translator/reference/rest-api-guide)
- [ASP.NET Core Configuration](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/)
- [Azure Managed Identity](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
- [Azure AI Services RBAC](https://learn.microsoft.com/azure/ai-services/authentication)
