// ==========================================
// Main Orchestration Template
// ==========================================
// This template orchestrates all infrastructure modules
// for the SignalR Chat application

targetScope = 'resourceGroup'

@description('The environment to deploy')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

@description('The Azure region for resources')
param location string

@description('Short location abbreviation for resource names (e.g., plc for polandcentral)')
param shortLocation string

@description('The base name for all resources')
param baseName string

@description('Virtual Network address prefix')
param vnetAddressPrefix string

@description('App Service subnet address prefix')
param appServiceSubnetPrefix string

@description('Private Endpoints subnet address prefix')
param privateEndpointsSubnetPrefix string

@description('Azure Communication Services data location')
param acsDataLocation string

@description('The networking resource group name')
param networkingResourceGroupName string

@description('Custom DNS servers for the VNet (optional, comma-separated). If empty, uses Azure default DNS.')
param vnetDnsServers string = ''

@description('OTP pepper for secure hashing (REQUIRED)')
@secure()
param otpPepper string

@description('Entra ID instance base URL (blank to use default)')
param entraIdInstance string = ''

@description('Entra ID tenant ID or "organizations" for multi-tenant')
param entraIdTenantId string = 'organizations'

@description('Entra ID sign-in callback path')
param entraIdCallbackPath string = '/signin-oidc'

@description('Entra ID sign-out callback path')
param entraIdSignedOutCallbackPath string = '/signout-callback-oidc'

@description('Require tenant validation against allowed list')
param entraIdRequireTenantValidation bool = true

@description('Allowed tenant IDs array for multi-tenant auth')
param entraIdAllowedTenants array = []

@description('Enable automatic silent SSO attempt')
param entraIdAutomaticSsoEnable bool = false

@description('Cookie name recording silent SSO attempt')
param entraIdAutomaticSsoAttemptCookieName string = 'sso_attempted'

@description('Enable OTP fallback if Entra ID unavailable')
param entraIdFallbackEnableOtp bool = true

@description('Allow OTP for unauthorized tenant users')
param entraIdFallbackOtpForUnauthorizedUsers bool = false

@description('Optional Entra ID connection string (ClientId=...;ClientSecret=...)')
@secure()
param entraIdConnectionString string = ''

@description('Home tenant ID for admin panel authorization')
param entraIdAuthorizationHomeTenantId string = ''

@description('App Role value required for admin access')
param entraIdAuthorizationAdminRoleValue string = 'Admin.ReadWrite'

@description('Translation provider: NMT (Neural Machine Translation), LLM-GPT4oMini, or LLM-GPT4o')
@allowed([
  'NMT'
  'LLM-GPT4oMini'
  'LLM-GPT4o'
])
param translationProvider string = 'LLM-GPT4oMini'

@description('Enable translation service')
param enableTranslation bool = false

// ==========================================
// Variables - Static IP Allocation
// ==========================================
// Calculate static IPs for private endpoints in the Private Endpoints subnet
// Azure reserves the first 4 IPs (.0, .1, .2, .3) in each subnet, so we start from subnet_base + 4
// Format: Extract subnet IP (before CIDR), parse last octet, add offset (4, 5, 6, 7)

// Step 1: Extract subnet IP without CIDR (e.g., "10.50.8.32/27" -> "10.50.8.32")
var peSubnetIp = substring(privateEndpointsSubnetPrefix, 0, indexOf(privateEndpointsSubnetPrefix, '/'))

// Step 2: Extract base IP (first 3 octets) and last octet
var peSubnetLastDotIndex = lastIndexOf(peSubnetIp, '.')
var peSubnetBase = substring(peSubnetIp, 0, peSubnetLastDotIndex)
var peSubnetLastOctet = int(substring(peSubnetIp, peSubnetLastDotIndex + 1, length(peSubnetIp) - peSubnetLastDotIndex - 1))

// Step 3: Calculate static IPs by adding offset to subnet's last octet
// Cosmos DB: 2 IPs (global + regional endpoint)
var cosmosPrivateIp1 = '${peSubnetBase}.${peSubnetLastOctet + 4}'
var cosmosPrivateIp2 = '${peSubnetBase}.${peSubnetLastOctet + 5}'
// Redis: 1 IP (generic endpoint only)
var redisPrivateIp = '${peSubnetBase}.${peSubnetLastOctet + 6}'
// SignalR: 1 IP (generic endpoint only)
var signalRPrivateIp = '${peSubnetBase}.${peSubnetLastOctet + 7}'
// App Service: 1 IP
var appServicePrivateIp = '${peSubnetBase}.${peSubnetLastOctet + 8}'

// App Service URL (deterministic, constructed before deployment)
var appServiceUrl = 'https://${baseName}-${environment}-${shortLocation}.azurewebsites.net'

// ==========================================
// Module: Networking (TWO Subnets)
// ==========================================
// Deploy to separate networking resource group
module networking './modules/networking.bicep' = {
  name: 'networking-deployment'
  scope: resourceGroup(networkingResourceGroupName)
  params: {
    vnetName: 'vnet-${baseName}-${environment}-${shortLocation}'
    location: location
    vnetAddressPrefix: vnetAddressPrefix
    appServiceSubnetPrefix: appServiceSubnetPrefix
    privateEndpointsSubnetPrefix: privateEndpointsSubnetPrefix
    dnsServers: !empty(vnetDnsServers) ? split(vnetDnsServers, ',') : []
  }
}

// ==========================================
// Module: Monitoring (Log Analytics + App Insights)
// ==========================================
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring-deployment'
  params: {
    baseName: baseName
    location: location
    shortLocation: shortLocation
    environment: environment
  }
}

// ==========================================
// Module: Cosmos DB
// ==========================================
module cosmosDb './modules/cosmos-db.bicep' = {
  name: 'cosmos-deployment'
  params: {
    accountName: 'cdb-${baseName}-${environment}-${shortLocation}'
    location: location
    environment: environment
    databaseName: 'chat'
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
    privateEndpointStaticIp1: cosmosPrivateIp1
    privateEndpointStaticIp2: cosmosPrivateIp2
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// ==========================================
// Module: Redis Enterprise
// ==========================================
// Using database-level listKeys to avoid broken cluster-level endpoint
// API version: 2025-07-01 with accessKeysAuthentication: Enabled
module redis './modules/redis.bicep' = {
  name: 'redis-deployment'
  params: {
    redisName: 'redis-${baseName}-${environment}-${shortLocation}'
    location: location
    environment: environment
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
    privateEndpointStaticIp: redisPrivateIp
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// ==========================================
// Module: SignalR Service
// ==========================================
module signalR './modules/signalr.bicep' = {
  name: 'signalr-deployment'
  params: {
    signalRName: 'sigr-${baseName}-${environment}-${shortLocation}'
    location: location
    environment: environment
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
    privateEndpointStaticIp: signalRPrivateIp
    // Allow App Service URL always; in dev also allow local origin for browser-based development
    allowedOrigins: environment == 'dev' ? [
      appServiceUrl
      'https://localhost:5099'
      'http://localhost:5099'
    ] : [
      appServiceUrl
    ]
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// ==========================================
// Module: Azure Communication Services
// ==========================================
module acs './modules/communication.bicep' = {
  name: 'acs-deployment'
  params: {
    acsName: 'acs-${baseName}-${environment}'
    emailServiceName: 'acs-email-${baseName}-${environment}'
    dataLocation: acsDataLocation
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// ==========================================
// Module: AI Translation Service (Optional)
// ==========================================
module translation './modules/translation.bicep' = if (enableTranslation) {
  name: 'translation-deployment'
  params: {
    baseName: baseName
    environment: environment
    location: location
    shortLocation: shortLocation
    translationProvider: translationProvider
    sku: environment == 'prod' ? 'S0' : 'S0' // S0 for all environments (F0 has low quotas)
    publicNetworkAccess: environment == 'dev' // Public in dev, private in staging/prod
    disableLocalAuth: false // Keep key-based auth for simplicity
    privateEndpointSubnetId: environment == 'dev' ? '' : networking.outputs.privateEndpointsSubnetId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// ==========================================
// Module: App Service (depends on all above)
// ==========================================
module appService './modules/app-service.bicep' = {
  name: 'app-service-deployment'
  params: {
    appName: '${baseName}-${environment}-${shortLocation}'
    location: location
    environment: environment
    vnetIntegrationSubnetId: networking.outputs.appServiceSubnetId
    appInsightsConnectionString: monitoring.outputs.connectionString
    appInsightsInstrumentationKey: monitoring.outputs.instrumentationKey
    cosmosConnectionString: cosmosDb.outputs.connectionString
    redisConnectionString: redis.outputs.connectionString
    signalRConnectionString: signalR.outputs.connectionString
    acsConnectionString: acs.outputs.connectionString
    acsSenderEmailAddress: acs.outputs.senderEmailAddress
    otpPepper: otpPepper
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
    privateEndpointStaticIp: appServicePrivateIp
    // Entra ID parameters
    entraIdInstance: entraIdInstance
    entraIdTenantId: entraIdTenantId
    entraIdCallbackPath: entraIdCallbackPath
    entraIdSignedOutCallbackPath: entraIdSignedOutCallbackPath
    entraIdRequireTenantValidation: entraIdRequireTenantValidation
    entraIdAllowedTenants: entraIdAllowedTenants
    entraIdAutomaticSsoEnable: entraIdAutomaticSsoEnable
    entraIdAutomaticSsoAttemptCookieName: entraIdAutomaticSsoAttemptCookieName
    entraIdFallbackEnableOtp: entraIdFallbackEnableOtp
    entraIdFallbackOtpForUnauthorizedUsers: entraIdFallbackOtpForUnauthorizedUsers
    entraIdConnectionString: entraIdConnectionString
    entraIdAuthorizationHomeTenantId: entraIdAuthorizationHomeTenantId
    entraIdAuthorizationAdminRoleValue: entraIdAuthorizationAdminRoleValue
    // Translation parameters (conditional)
    translationEnabled: enableTranslation
    translationResourceId: enableTranslation ? translation!.outputs.resourceId : ''
    translationEndpoint: enableTranslation ? translation!.outputs.endpoint : ''
    translationProvider: enableTranslation ? translation!.outputs.translationProvider : ''
    translationModelDeploymentName: enableTranslation ? translation!.outputs.modelDeploymentName : ''
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The URL of the deployed application')
output appUrl string = 'https://${appService.outputs.defaultHostName}'

@description('The resource group name')
output resourceGroupName string = resourceGroup().name

@description('The environment deployed')
output environment string = environment

@description('Virtual Network ID')
output vnetId string = networking.outputs.vnetId

@description('App Service Subnet ID')
output appServiceSubnetId string = networking.outputs.appServiceSubnetId

@description('Private Endpoints Subnet ID')
output privateEndpointsSubnetId string = networking.outputs.privateEndpointsSubnetId

@description('Translation service enabled')
output translationEnabled bool = enableTranslation

@description('Translation endpoint (if enabled)')
output translationEndpoint string = enableTranslation ? translation!.outputs.endpoint : ''

@description('Translation provider (NMT, LLM-GPT4oMini, or LLM-GPT4o)')
output translationProvider string = enableTranslation ? translation!.outputs.translationProvider : ''
