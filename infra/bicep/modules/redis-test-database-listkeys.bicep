// ==========================================
// Redis Test - Database-level listKeys
// ==========================================
// This is a test file to verify that using database-level listKeys
// resolves the "localPath required" error

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
    publicNetworkAccess: 'Enabled'
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
  }
}

// ==========================================
// Outputs - USING DATABASE-LEVEL listKeys
// ==========================================
@description('The resource ID of the Redis Enterprise cluster')
output redisId string = redisEnterprise.id

@description('The name of the Redis Enterprise cluster')
output redisName string = redisEnterprise.name

@description('The hostname of the Redis Enterprise cluster')
output hostName string = redisEnterprise.properties.hostName

@description('The Redis connection string - USING DATABASE listKeys')
@secure()
output connectionString string = '${redisEnterprise.properties.hostName}:${redisEnterpriseDatabase.properties.port},password=${redisEnterpriseDatabase.listKeys().primaryKey},ssl=True,abortConnect=False'
