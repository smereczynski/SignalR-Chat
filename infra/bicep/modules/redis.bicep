// ==========================================
// Azure Managed Redis Module
// ==========================================
// This module creates Azure Managed Redis (redisEnterprise)
// NOT Azure Cache for Redis (the legacy service)
//
// Azure Managed Redis uses Microsoft.Cache/redisEnterprise resource type
//
// IMPORTANT: listKeys is a DATABASE-level operation, not cluster-level
// - Correct: redisEnterpriseDatabase.listKeys() ✅
// - API: /databases/{databaseName}/listKeys

@description('The name of the Azure Managed Redis cluster')
param redisName string

@description('The location for all resources')
param location string = resourceGroup().location

@description('The environment (dev, staging, prod)')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

@description('Subnet ID for private endpoint (optional)')
param privateEndpointSubnetId string = ''

@description('Static IP address for private endpoint (optional)')
param privateEndpointStaticIp string = ''

@description('Log Analytics Workspace ID for diagnostic logs')
param logAnalyticsWorkspaceId string = ''

// ==========================================
// Variables
// ==========================================
// SKU: dev=Balanced_B1, staging=Balanced_B3, prod=Balanced_B5
var skuName = environment == 'prod' ? 'Balanced_B5' : (environment == 'staging' ? 'Balanced_B3' : 'Balanced_B1')

// ==========================================
// Azure Managed Redis Cluster
// ==========================================
resource redisEnterprise 'Microsoft.Cache/redisEnterprise@2025-07-01' = {
  name: redisName
  location: location
  sku: {
    name: skuName
  }
  identity: {
    type: 'None'
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: privateEndpointSubnetId != '' ? 'Disabled' : 'Enabled'
  }
}

// ==========================================
// Redis Database
// ==========================================
resource redisEnterpriseDatabase 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = {
  parent: redisEnterprise
  name: 'default'
  properties: {
    accessKeysAuthentication: 'Enabled'
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'NoEviction'
  }
}

// ==========================================
// Private Endpoint
// ==========================================
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-10-01' = if (privateEndpointSubnetId != '') {
  name: 'pe-${redisName}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${redisName}-connection'
        properties: {
          privateLinkServiceId: redisEnterprise.id
          groupIds: [
            'redisEnterprise'
          ]
        }
      }
    ]
    customNetworkInterfaceName: 'nic-pe-${redisName}'
    ipConfigurations: privateEndpointStaticIp != '' ? [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAddress: privateEndpointStaticIp
          groupId: 'redisEnterprise'
          memberName: 'redisEnterprise'
        }
      }
    ] : []
  }
}

// ==========================================
// Diagnostic Settings
// ==========================================
// Note: Logs are only available on the database resource, not the cluster
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (logAnalyticsWorkspaceId != '') {
  name: 'diagnostics-${redisName}-db'
  scope: redisEnterpriseDatabase
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'ConnectionEvents'
        enabled: true
      }
    ]
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The resource ID of the Redis Enterprise cluster')
output redisId string = redisEnterprise.id

@description('The name of the Redis Enterprise cluster')
output redisName string = redisEnterprise.name

@description('The hostname of the Redis Enterprise cluster')
output hostName string = redisEnterprise.properties.hostName

@description('The Redis connection string')
@secure()
output connectionString string = '${redisEnterprise.properties.hostName}:${redisEnterpriseDatabase.properties.port},password=${redisEnterpriseDatabase.listKeys().primaryKey},ssl=True,abortConnect=False'
