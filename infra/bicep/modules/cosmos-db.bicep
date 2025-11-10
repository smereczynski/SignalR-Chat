// ==========================================
// Cosmos DB Module - NoSQL API
// ==========================================
// This module creates:
// - Cosmos DB Account (NoSQL API)
// - Database with containers: messages, users, rooms
// - Supports both provisioned throughput and autoscale

@description('The name of the Cosmos DB account')
param accountName string

@description('The location for the Cosmos DB account')
param location string = resourceGroup().location

@description('The environment (dev, staging, prod)')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

@description('The name of the database')
param databaseName string = 'chat'

@description('Subnet ID for private endpoint (optional)')
param privateEndpointSubnetId string = ''

@description('Static IP address for private endpoint - ipconfig1 (optional)')
param privateEndpointStaticIp1 string = ''

@description('Static IP address for private endpoint - ipconfig2 (optional)')
param privateEndpointStaticIp2 string = ''

@description('Log Analytics Workspace ID for diagnostic logs')
param logAnalyticsWorkspaceId string = ''

// ==========================================
// Variables
// ==========================================
var consistencyPolicy = {
  defaultConsistencyLevel: 'Session'
  maxIntervalInSeconds: 5
  maxStalenessPrefix: 100
}

// Zone redundancy: dev = false, staging/prod = true
var isZoneRedundant = environment != 'dev'

// Single region deployment (polandcentral) for all environments
var locations = [
  {
    locationName: location
    failoverPriority: 0
    isZoneRedundant: isZoneRedundant
  }
]

// ==========================================
// Cosmos DB Account
// ==========================================
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: consistencyPolicy
    locations: locations
    enableAutomaticFailover: false
    enableMultipleWriteLocations: false
    publicNetworkAccess: privateEndpointSubnetId != '' ? 'Disabled' : 'Enabled'
    capabilities: []
  }
}

// ==========================================
// Database
// ==========================================
resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
    options: {
      autoscaleSettings: {
        maxThroughput: environment == 'prod' ? 4000 : (environment == 'staging' ? 4000 : 1000)
      }
    }
  }
}

// ==========================================
// Container: messages
// ==========================================
resource messagesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'messages'
  properties: {
    resource: {
      id: 'messages'
      partitionKey: {
        paths: [
          '/roomId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
      defaultTtl: -1
    }
  }
}

// ==========================================
// Container: users
// ==========================================
resource usersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'users'
  properties: {
    resource: {
      id: 'users'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
      uniqueKeyPolicy: {
        uniqueKeys: [
          {
            paths: [
              '/phoneNumber'
            ]
          }
        ]
      }
    }
  }
}

// ==========================================
// Container: rooms
// ==========================================
resource roomsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'rooms'
  properties: {
    resource: {
      id: 'rooms'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
  }
}

// ==========================================
// Private Endpoint
// ==========================================
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-10-01' = if (privateEndpointSubnetId != '') {
  name: 'pe-${accountName}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${accountName}-connection'
        properties: {
          privateLinkServiceId: cosmosAccount.id
          groupIds: [
            'Sql'
          ]
        }
      }
    ]
    customNetworkInterfaceName: 'nic-pe-${accountName}'
    ipConfigurations: privateEndpointStaticIp1 != '' ? [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAddress: privateEndpointStaticIp1
          groupId: 'Sql'
          memberName: accountName
        }
      }
      {
        name: 'ipconfig2'
        properties: {
          privateIPAddress: privateEndpointStaticIp2
          groupId: 'Sql'
          memberName: '${accountName}-${location}'
        }
      }
    ] : []
  }
}

// ==========================================
// Diagnostic Settings
// ==========================================
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (logAnalyticsWorkspaceId != '') {
  name: 'diagnostics-${accountName}'
  scope: cosmosAccount
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
// Outputs
// ==========================================
@description('The resource ID of the Cosmos DB account')
output accountId string = cosmosAccount.id

@description('The name of the Cosmos DB account')
output accountName string = cosmosAccount.name

@description('The endpoint URL for the Cosmos DB account')
output endpoint string = cosmosAccount.properties.documentEndpoint

@description('The connection string for the Cosmos DB account')
@secure()
output connectionString string = 'AccountEndpoint=${cosmosAccount.properties.documentEndpoint};AccountKey=${cosmosAccount.listKeys().primaryMasterKey}'

@description('The primary master key')
@secure()
output primaryKey string = cosmosAccount.listKeys().primaryMasterKey

@description('The database name')
output databaseName string = database.name
