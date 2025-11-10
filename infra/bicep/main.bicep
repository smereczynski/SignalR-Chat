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
    allowedOrigins: [
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
    dataLocation: acsDataLocation
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
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
    privateEndpointStaticIp: appServicePrivateIp
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
