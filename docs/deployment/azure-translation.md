# Azure AI Translation Service

Deploy **Microsoft Foundry** (Azure AI Services) for real-time message translation.

## Overview

The translation infrastructure supports both:
- **LLM-based Translation**: GPT-4o-mini or GPT-4o models (default, higher quality)
- **Neural Machine Translation (NMT)**: Standard Azure Translator API (cost-effective fallback)

## Resource: Microsoft Foundry

**Azure AI Services (Foundry)**:
- Resource Type: `Microsoft.CognitiveServices/accounts`
- Kind: `AIServices` (multi-service unified resource)
- API Version: `2025-06-01`
- Location: `polandcentral` (Poland Central) ✅

Provides unified access to:
- Azure OpenAI (GPT-4o-mini, GPT-4o) - default
- Azure AI Translator (NMT)
- Text Analytics
- Language Understanding

## Translation Providers

### 1. NMT (Neural Machine Translation)

**Best for**: Cost-sensitive scenarios, high-volume translation

- Uses Azure AI Translator API
- **Cost**: ~$10/million characters (first 2M free)
- **Latency**: ~100-300ms per request
- **Quality**: Good for general chat messages
- **API**: REST API v3.0

### 2. LLM-GPT4oMini - Default

**Best for**: Chat messages, context-aware translation, informal language/slang

- Uses GPT-4o-mini model
- **Cost**: ~$0.15/1M input tokens + $0.60/1M output tokens
- **Latency**: ~500-1000ms per request
- **Quality**: Better context understanding, handles slang/idioms
- **API**: Azure OpenAI Chat Completions API

### 3. LLM-GPT4o

**Best for**: Highest quality, complex conversations, domain-specific terminology

- Uses GPT-4o model (16x more expensive than GPT-4o-mini)
- **Cost**: ~$2.50/1M input tokens + $10/1M output tokens
- **Latency**: ~800-1500ms per request
- **Quality**: Best available

## Deployment

### Bicep Configuration

The translation module is located at `infra/bicep/modules/translation.bicep`.

The module deploys the AI Services account with a public endpoint (network ACL `defaultAction: Allow`).

**Parameters**:

```bicep
module translation './modules/translation.bicep' = if (enableTranslation) {
  name: 'translation-deployment'
  params: {
    baseName: 'signalrchat'
    environment: 'dev'                           // dev, staging, prod
    location: 'polandcentral'
    translationProvider: 'LLM-GPT4oMini'         // Default: LLM-GPT4oMini, also: NMT, LLM-GPT4o
    sku: 'S0'                                    // F0 (free, limited), S0 (standard)
    disableLocalAuth: false                      // true for Entra ID only
  }
}
```

### Enable Translation

In parameter files (`main.parameters.<env>.bicepparam`):

```bicep
// Enable translation service
param enableTranslation = true

// Select provider
param translationProvider = 'LLM-GPT4oMini'  // Default: LLM-based translation
```

### Deploy Infrastructure

```bash
cd infra/bicep

# Dev environment
az deployment group create \
  --resource-group rg-signalrchat-dev-plc \
  --template-file main.bicep \
  --parameters @main.parameters.dev.bicepparam

# Get translation endpoint
az deployment group show \
  --resource-group rg-signalrchat-dev-plc \
  --name <deployment-name> \
  --query properties.outputs.translationEndpoint.value
```

## App Service Configuration

Translation settings are automatically added as app settings:

```bash
Translation__Enabled=true
Translation__Endpoint=https://ai-translation-signalrchat-dev.cognitiveservices.azure.com/
Translation__ApiKey=<api-key>
Translation__Provider=LLM-GPT4oMini  # Default: LLM-GPT4oMini, also: NMT, LLM-GPT4o
Translation__ModelDeploymentName=gpt-4o-mini  # empty for NMT, model name for LLM
```

## API Usage

### NMT Translation (REST API)

```bash
# Translate "Hello world" to Polish and German
curl -X POST "https://<endpoint>/translator/text/v3.0/translate?api-version=3.0&to=pl&to=de" \
  -H "Ocp-Apim-Subscription-Key: <apiKey>" \
  -H "Content-Type: application/json" \
  -d '[{"text": "Hello world"}]'
```

Response:
```json
[
  {
    "detectedLanguage": {"language": "en", "score": 1.0},
    "translations": [
      {"text": "Witaj świecie", "to": "pl"},
      {"text": "Hallo Welt", "to": "de"}
    ]
  }
]
```

### LLM Translation (OpenAI API)

```bash
curl -X POST "https://<endpoint>/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-08-01-preview" \
  -H "api-key: <apiKey>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [
      {
        "role": "system",
        "content": "You are a translator. Translate the following chat message from English to Polish. Preserve emoji and formatting. Only output the translated text."
      },
      {
        "role": "user",
        "content": "Hey what'\''s up? 🙂"
      }
    ],
    "temperature": 0.3,
    "max_tokens": 500
  }'
```

Response:
```json
{
  "choices": [
    {
      "message": {
        "role": "assistant",
        "content": "Hej, co słychać? 🙂"
      }
    }
  ]
}
```

## Cost Estimates

### Development (1M messages/month, avg 50 chars/message)

| Provider | Cost/Month | Notes |
|----------|-----------|-------|
| NMT | $2-5 | First 2M chars free, then $10/1M chars |
| LLM-GPT4oMini | $50-100 | ~$0.15/1M input tokens |
| LLM-GPT4o | $200-300 | ~$2.50/1M input tokens |

### Production (10M messages/month, avg 50 chars/message)

| Provider | Cost/Month | Notes |
|----------|-----------|-------|
| NMT | $50-100 | 500M chars = $5-10 per 1M |
| LLM-GPT4oMini | $500-800 | Context + translations |
| LLM-GPT4o | $2000-3000 | Highest quality |

## Supported Languages

All 9 languages in the application:
- English (en), Polish (pl), German (de), French (fr), Spanish (es)
- Italian (it), Portuguese (pt), Japanese (ja), Chinese (zh)

## Migration Path

**Phase 1**: Start with LLM-GPT4oMini (default)
- Deploy with `translationProvider: 'LLM-GPT4oMini'`
- Test translation quality and latency
- Monitor costs (~$50-100/month in dev)

**Phase 2**: Cost Optimization (if needed)
- If budget constrained: Switch to `translationProvider: 'NMT'`
- Compare quality degradation vs cost savings
- Monitor user feedback

**Phase 3**: Quality Upgrade (if needed)
- If quality critical: Upgrade to `translationProvider: 'LLM-GPT4o'`
- Highest quality, higher cost (~$200-300/month)
- Consider for production only

## Security

- **API Keys**: Stored as secure outputs, passed to App Service
- **Managed Identity**: System-assigned identity enabled for Entra ID auth
- **Network**: Supports private endpoints (set `publicNetworkAccess: false`)
- **Local Auth**: Can be disabled for Entra ID-only access

## Monitoring

Key metrics to track:
- Translation requests/min
- API latency (p50, p95, p99)
- Error rate
- Token usage (for LLM providers)
- Cost per day/month

## Troubleshooting

### Error: "Deployment not found"
- Ensure `translationProvider` is set to LLM variant (not NMT)
- Check model deployment name matches output

### Error: "Rate limit exceeded"
- Increase SKU capacity (10 TPM → 50 TPM)
- Add retry logic with exponential backoff
- Consider scaling to multiple regions

### Error: "Invalid API version"
- NMT: Use `api-version=3.0`
- LLM: Use `api-version=2024-08-01-preview`

### High Latency
- LLM translation slower than NMT (expected)
- Consider caching common translations
- Use async processing for bulk translations

## References

- [Azure AI Translator Docs](https://learn.microsoft.com/azure/ai-services/translator/)
- [Azure OpenAI Service Docs](https://learn.microsoft.com/azure/ai-services/openai/)
- [Microsoft Foundry Overview](https://learn.microsoft.com/azure/ai-foundry/what-is-azure-ai-foundry)
- [Translation API Reference](https://learn.microsoft.com/azure/ai-services/translator/reference/v3-0-translate)
- [Bicep Module: translation.bicep](../../infra/bicep/modules/translation.bicep)

[Back to Deployment](README.md) | [Back to Documentation](../README.md)
