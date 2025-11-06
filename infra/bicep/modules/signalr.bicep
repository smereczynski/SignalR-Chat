// ==========================================
// SignalR Service Module
// ==========================================
// This module creates Azure SignalR Service

@description('The name of the SignalR service')
param signalRName string

@description('The location for the SignalR service')
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

@description('Tags to apply to all resources')

// ==========================================
// Variables
// ==========================================
// SKU: Standard_S1 for all environments (Free tier not suitable for production workloads)
// Capacity varies by environment: dev=1, staging=1, prod=5
var sku = {
  name: 'Standard_S1'
  tier: 'Standard'
  capacity: environment == 'prod' ? 5 : 1
}

// ==========================================
// SignalR Service
// ==========================================
resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: signalRName
  location: location
  sku: sku
  kind: 'SignalR'
  properties: {
    tls: {
      clientCertEnabled: false
    }
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'True'
      }
      {
        flag: 'EnableMessagingLogs'
        value: 'True'
      }
    ]
    cors: {
      allowedOrigins: [
        '*'
      ]
    }
    publicNetworkAccess: privateEndpointSubnetId != '' ? 'Disabled' : 'Enabled'
    disableLocalAuth: false
    disableAadAuth: false
  }
}

// ==========================================
// Private Endpoint
// ==========================================
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (privateEndpointSubnetId != '') {
  name: 'pe-${signalRName}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${signalRName}'
        properties: {
          privateLinkServiceId: signalR.id
          groupIds: [
            'signalr'
          ]
        }
      }
    ]
    customNetworkInterfaceName: 'nic-pe-${signalRName}'
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The resource ID of the SignalR service')
output signalRId string = signalR.id

@description('The name of the SignalR service')
output signalRName string = signalR.name

@description('The hostname of the SignalR service')
output hostName string = signalR.properties.hostName

@description('The connection string for the SignalR service')
@secure()
output connectionString string = signalR.listKeys().primaryConnectionString

@description('The primary access key')
@secure()
output primaryKey string = signalR.listKeys().primaryKey
