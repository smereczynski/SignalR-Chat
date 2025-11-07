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
  }
}

// ==========================================
// Module: Redis Cache
// ==========================================
module redis './modules/redis.bicep' = {
  name: 'redis-deployment'
  params: {
    redisName: 'redis-${baseName}-${environment}-${shortLocation}'
    location: location
    environment: environment
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
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
