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

@description('Short location abbreviation for resource names (e.g., plc for polandcentral)')
param shortLocation string

@description('Translation provider: NMT (Neural Machine Translation), LLM-GPT4oMini, or LLM-GPT4o')
@allowed([
  'NMT'
  'LLM-GPT4oMini'
  'LLM-GPT4o'
])
param translationProvider string = 'LLM-GPT4oMini'

@description('SKU for AI Services account')
@allowed([
  'F0' // Free tier
  'S0' // Standard tier
])
param sku string = 'S0'

@description('Disable local authentication (use Entra ID only)')
param disableLocalAuth bool = false

@description('Subnet ID for private endpoint (optional)')
param privateEndpointSubnetId string = ''

@description('Array of 3 static IP addresses for private endpoint (optional)')
param privateEndpointStaticIps array = []

@description('VPN IP address for firewall rules (optional, only for dev environment)')
param vpnIpAddress string = ''

@description('Log Analytics Workspace ID for diagnostic logs')
param logAnalyticsWorkspaceId string = ''

// =========================================
// Resource Naming
// =========================================

var aiServicesName = 'aif-${baseName}-${environment}-${shortLocation}'
var customSubDomainName = 'aif-${baseName}-${environment}-${shortLocation}'

// =========================================
// Microsoft Foundry (AI Services) Account
// =========================================
// This resource provides access to:
// - Azure AI Translator (standard NMT)
// - LLM-based translation (GPT-4o-mini, GPT-4o)
// - Text analytics, language understanding

// Build IP rules array for dev environment only
// Dev: Allow VPN IP + Azure Portal IPs (for manual management)
// Staging/Prod: No IP rules (private endpoint only)
var ipRulesArray = environment == 'dev' && !empty(vpnIpAddress) ? [
  {
    value: vpnIpAddress
  }
] : []

resource aiServices 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: aiServicesName
  location: location
  sku: {
    name: sku
  }
  kind: 'AIServices' // Foundry resource type
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // Custom subdomain required for API access
    customSubDomainName: customSubDomainName
    
    // Network and authentication settings
    // All environments: Enable public access with firewall rules
    // Private endpoint provides additional secure access path
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: disableLocalAuth
    
    // Enable project management for Foundry
    allowProjectManagement: true
    
    // Network ACLs - configure IP rules and trusted services
    // Dev with VPN IP: Deny all except VPN IP + Azure services
    // Dev without VPN IP: Allow all (for local development)
    // Staging/Prod: Deny all except Azure services (private endpoint provides access)
    networkAcls: {
      defaultAction: (environment == 'dev' && !empty(vpnIpAddress)) || (environment != 'dev') ? 'Deny' : 'Allow'
      ipRules: ipRulesArray
      bypass: 'AzureServices' // Allow Azure services bypass
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
// Private Endpoint (Optional)
// =========================================
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (privateEndpointSubnetId != '') {
  name: 'pe-${aiServicesName}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${aiServicesName}'
        properties: {
          privateLinkServiceId: aiServices.id
          groupIds: [
            'account'
          ]
        }
      }
    ]
    // Use 3 static IPs with unique memberNames (default, secondary, third)
    ipConfigurations: length(privateEndpointStaticIps) == 3 ? [
      {
        name: 'ipconfig-${aiServicesName}-default'
        properties: {
          privateIPAddress: privateEndpointStaticIps[0]
          groupId: 'account'
          memberName: 'default' // cognitiveservices.azure.com
        }
      }
      {
        name: 'ipconfig-${aiServicesName}-secondary'
        properties: {
          privateIPAddress: privateEndpointStaticIps[1]
          groupId: 'account'
          memberName: 'secondary' // openai.azure.com
        }
      }
      {
        name: 'ipconfig-${aiServicesName}-third'
        properties: {
          privateIPAddress: privateEndpointStaticIps[2]
          groupId: 'account'
          memberName: 'third' // services.ai.azure.com
        }
      }
    ] : []
    customNetworkInterfaceName: 'nic-pe-${aiServicesName}'
  }
}

// =========================================
// Diagnostic Settings
// =========================================
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (logAnalyticsWorkspaceId != '') {
  scope: aiServices
  name: 'diagnostics-${aiServicesName}'
  properties: {
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    workspaceId: logAnalyticsWorkspaceId
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
