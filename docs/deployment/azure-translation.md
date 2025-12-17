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
- **API**: Translator `translate` endpoint (no `deploymentName` required)

### 2. LLM-GPT4oMini - Default

**Best for**: Chat messages, context-aware translation, informal language/slang

- Uses GPT-4o-mini model
- **Cost**: ~$0.15/1M input tokens + $0.60/1M output tokens
- **Latency**: ~500-1000ms per request
- **Quality**: Better context understanding, handles slang/idioms
- **API**: Translator `translate` endpoint with `deploymentName` (LLM deployment)

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

## API Usage (Used by the App)

The application uses the Translator `translate` endpoint with the configured `Translation__ApiVersion` (default: `2025-10-01-preview`).

```bash
curl -X POST "https://<endpoint>/translator/text/translate?api-version=2025-10-01-preview" \
  -H "Ocp-Apim-Subscription-Key: <apiKey>" \
  -H "Ocp-Apim-Subscription-Region: <region>" \
  -H "Content-Type: application/json" \
  -d '{
    "inputs": [
      {
        "text": "bry!",
        "targets": [
          { "language": "en", "deploymentName": "gpt-4o-mini", "tone": "casual" },
          { "language": "pl", "deploymentName": "gpt-4o-mini", "tone": "casual" }
        ]
      }
    ]
  }'
```

Notes:
- Omitting `language` enables **auto-detect** (the service returns detected language + confidence score).
- For NMT, `deploymentName` is typically not required; for LLM deployments it must match your configured model deployment.

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

All languages currently enabled in the application localization layer:
- English (en)
- Polish (pl)
- German (de)
- Czech (cs)
- Slovak (sk)
- Ukrainian (uk)
- Lithuanian (lt)
- Russian (ru)

## How the App Chooses Languages

- **Source language**: sender's user preference (`preferredLanguage`) when set; otherwise `auto`.
- **Target languages**: the room's language set (`languages`) plus **always** English (`en`).
- **No invalid targets**: `auto` is never sent as a target language.
- **No retroactive translation**: room language changes apply only to new messages.

## Unknown Words, Names, and Very Short Strings

Chat messages often contain **names**, **typos**, **slang**, **abbreviations**, or fully **out-of-vocabulary (OOV)** strings (for example: `bry!`).

### What to Expect

- **LLM provider (GPT)**: Typically preserves OOV tokens and formatting better.
  - In practice, if a token can't be translated (brand name, typo, code-like string), the output often keeps it unchanged.
- **NMT provider (classic Translator)**: May **copy through** unknown tokens (especially named entities/acronyms), but this behavior is **not guaranteed**.
  - Microsoft notes that “as-is” copying isn't guaranteed in some dictionary scenarios, and the system can inflect/case-normalize text.

### Why This Happens

- **Auto-detect on short inputs can be low confidence**.
  - If you omit the source language (`from`), the service returns a detected language and a confidence `score`.
  - Short or non-language strings can yield low scores, which can lead to unstable translations.

### Recommendations (This Project)

- Prefer setting user `preferredLanguage` to avoid auto-detect on short messages.
- Treat “unchanged output” as a valid outcome for OOV strings.
- If you must guarantee copy-through for specific terms (product names, acronyms), use Translator dictionary features (phrase/verbatim dictionaries) on the NMT path.

## Migration Path

**Phase 1**: Start with LLM-GPT4oMini (default)
- Deploy with `translationProvider: 'LLM-GPT4oMini'`
- Test translation quality and latency
- Monitor costs (~$50-100/month in dev)

**Phase 2**: Cost Optimization (if needed)
- If budget constrained: Switch to `translationProvider: 'NMT'`
- Compare quality degradation vs cost savings
- Monitor user feedback
[Back to Deployment](README.md) | [Back to Documentation](../README.md)
