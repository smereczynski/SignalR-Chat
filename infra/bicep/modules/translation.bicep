// =========================================
// Azure AI Translation Service Module
// =========================================
// Creates Microsoft Foundry (AI Services) resource with translation capabilities
// Supports both standard NMT and LLM-based translation (GPT-4o-mini, GPT-4o)

@description('Environment name (dev, staging, prod)')
param environment string

@description('Base name for resources')
param baseName string

@description('Location for resources')
param location string = 'polandcentral'

@description('Translation provider: NMT (Neural Machine Translation), LLM-GPT4oMini, or LLM-GPT4o')
@allowed([
  'NMT'
  'LLM-GPT4oMini'
  'LLM-GPT4o'
])
param translationProvider string = 'NMT'

@description('SKU for AI Services account')
@allowed([
  'F0' // Free tier
  'S0' // Standard tier
])
param sku string = 'S0'

@description('Enable public network access')
param publicNetworkAccess bool = true

@description('Disable local authentication (use Entra ID only)')
param disableLocalAuth bool = false

// =========================================
// Resource Naming
// =========================================

var aiServicesName = 'ai-translation-${baseName}-${environment}'
var customSubDomainName = 'ai-translation-${baseName}-${environment}'

// =========================================
// Microsoft Foundry (AI Services) Account
// =========================================
// This resource provides access to:
// - Azure AI Translator (standard NMT)
// - LLM-based translation (GPT-4o-mini, GPT-4o)
// - Text analytics, language understanding

resource aiServices 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: aiServicesName
  location: location
  kind: 'AIServices' // Foundry resource type
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: sku
  }
  properties: {
    // Custom subdomain required for API access
    customSubDomainName: customSubDomainName
    
    // Network and authentication settings
    publicNetworkAccess: publicNetworkAccess ? 'Enabled' : 'Disabled'
    disableLocalAuth: disableLocalAuth
    
    // Enable project management for Foundry
    allowProjectManagement: true
    
    // Network ACLs (if public access disabled)
    networkAcls: publicNetworkAccess ? null : {
      defaultAction: 'Deny'
    }
  }
}

// =========================================
// Model Deployments (LLM-based translation)
// =========================================
// Deploy GPT-4o-mini for cost-effective LLM translation

resource gpt4oMiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = if (translationProvider == 'LLM-GPT4oMini') {
  parent: aiServices
  name: 'gpt-4o-mini'
  sku: {
    name: 'GlobalStandard'
    capacity: 10 // TPM (tokens per minute) in thousands
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18' // Latest stable version
    }
    raiPolicyName: 'Microsoft.DefaultV2' // Content filtering policy
  }
}

// Deploy GPT-4o for high-quality LLM translation

resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = if (translationProvider == 'LLM-GPT4o') {
  parent: aiServices
  name: 'gpt-4o'
  sku: {
    name: 'GlobalStandard'
    capacity: 10 // TPM (tokens per minute) in thousands
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20' // Latest stable version
    }
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

// =========================================
// Outputs
// =========================================

@description('Resource ID of the AI Services account')
output aiServicesId string = aiServices.id

@description('Name of the AI Services account')
output aiServicesName string = aiServices.name

@description('Resource ID of AI Services account')
output resourceId string = aiServices.id

@description('Endpoint URL for AI Services')
output endpoint string = aiServices.properties.endpoint

@description('Region/location of the resource')
output region string = aiServices.location

@description('Translation provider configured (NMT, LLM-GPT4oMini, or LLM-GPT4o)')
output translationProvider string = translationProvider

@description('Model deployment name (if LLM-based translation)')
output modelDeploymentName string = translationProvider == 'LLM-GPT4oMini' ? 'gpt-4o-mini' : (translationProvider == 'LLM-GPT4o' ? 'gpt-4o' : '')
