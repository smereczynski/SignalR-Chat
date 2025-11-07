// ==========================================
// Azure Managed Redis Module
// ==========================================
// This module creates Azure Managed Redis (redisEnterprise)
// NOT Azure Cache for Redis (the legacy service)
//
// Azure Managed Redis uses Microsoft.Cache/redisEnterprise resource type

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

// ==========================================
// Variables
// ==========================================
// SKU: dev=Balanced_B1, staging=Balanced_B3, prod=Balanced_B5
var skuName = environment == 'prod' ? 'Balanced_B5' : (environment == 'staging' ? 'Balanced_B3' : 'Balanced_B1')

// ==========================================
// Azure Managed Redis Cluster
// ==========================================
resource redisEnterprise 'Microsoft.Cache/redisEnterprise@2024-09-01-preview' = {
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
  }
}

// ==========================================
// Redis Database
// ==========================================
resource redisEnterpriseDatabase 'Microsoft.Cache/redisEnterprise/databases@2024-09-01-preview' = {
  parent: redisEnterprise
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'NoEviction'
    persistence: {
      aofEnabled: false
      rdbEnabled: false
    }
  }
}

// ==========================================
// Private Endpoint
// ==========================================
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (privateEndpointSubnetId != '') {
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
output connectionString string = '${redisEnterprise.properties.hostName}:${redisEnterpriseDatabase.properties.port},password=${redisEnterprise.listKeys().primaryKey},ssl=True,abortConnect=False'
