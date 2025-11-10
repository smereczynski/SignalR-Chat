// ==========================================
// App Service Module - App Service Plan + Web App
// ==========================================
// This module creates:
// - App Service Plan
// - VNet integration
// - App Settings and Connection Strings

@description('The name of the App Service')
param appName string

@description('The location for all resources')
param location string = resourceGroup().location

@description('The environment (dev, staging, prod)')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

@description('The subnet ID for VNet integration')
param vnetIntegrationSubnetId string

@description('Application Insights connection string')
@secure()
param appInsightsConnectionString string

@description('Application Insights instrumentation key')
@secure()
param appInsightsInstrumentationKey string

@description('Cosmos DB connection string')
@secure()
param cosmosConnectionString string

@description('Redis connection string')
@secure()
param redisConnectionString string

@description('SignalR connection string')
@secure()
param signalRConnectionString string

@description('Azure Communication Services connection string')
@secure()
param acsConnectionString string

@description('Log Analytics Workspace ID for diagnostic logs')
param logAnalyticsWorkspaceId string = ''

@description('Subnet ID for private endpoint (optional)')
param privateEndpointSubnetId string = ''

@description('Static IP address for private endpoint (optional)')
param privateEndpointStaticIp string = ''

// ==========================================
// Variables
// ==========================================
// SKU: dev=P0V4 (no zone redundancy), staging=P0V4 x2 (zone redundant), prod=P0V4 x3 (zone redundant)
var skuConfig = environment == 'prod' ? {
  name: 'P0V4'
  tier: 'PremiumV4'
  capacity: 3
  zoneRedundant: true
} : (environment == 'staging' ? {
  name: 'P0V4'
  tier: 'PremiumV4'
  capacity: 2
  zoneRedundant: true
} : {
  name: 'P0V4'
  tier: 'PremiumV4'
  capacity: 1
  zoneRedundant: false
})

// ==========================================
// App Service Plan
// ==========================================
resource appServicePlan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'serverfarm-${appName}'
  location: location
  sku: {
    name: skuConfig.name
    tier: skuConfig.tier
    capacity: skuConfig.capacity
  }
  kind: 'windows'
  properties: {
    zoneRedundant: skuConfig.zoneRedundant
  }
}

// ==========================================
// Web App
// ==========================================
resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: appName
  location: location
  kind: 'app'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: vnetIntegrationSubnetId
    siteConfig: {
      vnetRouteAllEnabled: true
      netFrameworkVersion: 'v9.0'
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
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~2'
        }
        {
          name: 'APPINSIGHTS_PROFILERFEATURE_VERSION'
          value: '1.0.0'
        }
        {
          name: 'APPINSIGHTS_SNAPSHOTFEATURE_VERSION'
          value: '1.0.0'
        }
        {
          name: 'DiagnosticServices_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'InstrumentationEngine_EXTENSION_VERSION'
          value: '~1'
        }
        {
          name: 'SnapshotDebugger_EXTENSION_VERSION'
          value: '~2'
        }
        {
          name: 'XDT_MicrosoftApplicationInsights_BaseExtensions'
          value: 'disabled'
        }
        {
          name: 'XDT_MicrosoftApplicationInsights_Mode'
          value: 'recommended'
        }
        {
          name: 'XDT_MicrosoftApplicationInsights_Java'
          value: '1'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'prod' ? 'Production' : (environment == 'staging' ? 'Production' : 'Development')
        }
        {
          name: 'Cosmos:Database'
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
  }
}

// ==========================================
// Diagnostic Settings for Web App
// ==========================================
resource webAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (logAnalyticsWorkspaceId != '') {
  name: 'diagnostics-${webApp.name}'
  scope: webApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
  }
}

// ==========================================
// Private Endpoint
// ==========================================
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-05-01' = if (privateEndpointSubnetId != '') {
  name: 'pe-${appName}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${appName}-connection'
        properties: {
          privateLinkServiceId: webApp.id
          groupIds: [
            'sites'
          ]
        }
      }
    ]
    customNetworkInterfaceName: 'nic-pe-${appName}'
    ipConfigurations: privateEndpointStaticIp != '' ? [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAddress: privateEndpointStaticIp
          groupId: 'sites'
          memberName: 'sites'
        }
      }
    ] : []
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The resource ID of the App Service Plan')
output appServicePlanId string = appServicePlan.id

@description('The name of the App Service Plan')
output appServicePlanName string = appServicePlan.name

@description('The resource ID of the Web App')
output webAppId string = webApp.id

@description('The name of the Web App')
output webAppName string = webApp.name

@description('The default hostname of the Web App')
output defaultHostName string = webApp.properties.defaultHostName
