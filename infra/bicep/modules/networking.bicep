// ==========================================
// Networking Module - Virtual Network with 2 Subnets
// ==========================================
// This module creates a Virtual Network with exactly TWO subnets:
// 1. App Service Integration Subnet (delegated to Microsoft.Web/serverFarms)
// 2. Private Endpoints Subnet
//
// Also creates Network Security Groups for both subnets.

@description('The name of the virtual network')
param vnetName string

@description('The location for all resources')
param location string = resourceGroup().location

@description('The address prefix for the virtual network (e.g., 10.0.0.0/16)')
param vnetAddressPrefix string

@description('The address prefix for the App Service integration subnet')
param appServiceSubnetPrefix string

@description('The address prefix for the Private Endpoints subnet')
param privateEndpointsSubnetPrefix string

// ==========================================
// Helper functions for subnet naming
// ==========================================
// Convert CIDR notation to subnet name format: xxx-xxx-xxx-xxx--yy
// Example: 10.0.0.0/27 -> 10-0-0-0--27
var appServiceSubnetName = replace(replace(appServiceSubnetPrefix, '.', '-'), '/', '--')
var privateEndpointsSubnetName = replace(replace(privateEndpointsSubnetPrefix, '.', '-'), '/', '--')

// ==========================================
// Network Security Group for App Service subnet
// ==========================================
resource nsgAppService 'Microsoft.Network/networkSecurityGroups@2024-10-01' = {
  name: 'nsg-${appServiceSubnetName}'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowHttpsOutbound'
        properties: {
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: '*'
          access: 'Allow'
          priority: 100
          direction: 'Outbound'
        }
      }
    ]
  }
}

// ==========================================
// Network Security Group for Private Endpoints Subnet
// ==========================================
resource privateEndpointsNsg 'Microsoft.Network/networkSecurityGroups@2024-10-01' = {
  name: 'nsg-${privateEndpointsSubnetName}'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowVnetInbound'
        properties: {
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: 'VirtualNetwork'
          access: 'Allow'
          priority: 100
          direction: 'Inbound'
        }
      }
    ]
  }
}

// ==========================================
// Route Table for App Service Subnet
// ==========================================
resource appServiceRouteTable 'Microsoft.Network/routeTables@2024-10-01' = {
  name: 'rt-${vnetName}-appservice'
  location: location
  properties: {
    routes: [
      {
        name: 'InternetRoute'
        properties: {
          addressPrefix: '0.0.0.0/0'
          nextHopType: 'Internet'
        }
      }
    ]
    disableBgpRoutePropagation: false
  }
}

// ==========================================
// Route Table for Private Endpoints Subnet
// ==========================================
resource privateEndpointsRouteTable 'Microsoft.Network/routeTables@2024-10-01' = {
  name: 'rt-${vnetName}-pe'
  location: location
  properties: {
    routes: []
    disableBgpRoutePropagation: false
  }
}

// ==========================================
// Virtual Network with TWO Subnets
// ==========================================
resource vnet 'Microsoft.Network/virtualNetworks@2024-10-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: appServiceSubnetName
        properties: {
          addressPrefix: appServiceSubnetPrefix
          networkSecurityGroup: {
            id: nsgAppService.id
          }
          routeTable: {
            id: appServiceRouteTable.id
          }
          delegations: [
            {
              name: 'Microsoft.Web.serverFarms'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
      {
        name: privateEndpointsSubnetName
        properties: {
          addressPrefix: privateEndpointsSubnetPrefix
          networkSecurityGroup: {
            id: privateEndpointsNsg.id
          }
          routeTable: {
            id: privateEndpointsRouteTable.id
          }
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The resource ID of the virtual network')
output vnetId string = vnet.id

@description('The name of the virtual network')
output vnetName string = vnet.name

@description('The resource ID of the App Service subnet')
output appServiceSubnetId string = vnet.properties.subnets[0].id

@description('The name of the App Service subnet')
output appServiceSubnetName string = vnet.properties.subnets[0].name

@description('The resource ID of the Private Endpoints subnet')
output privateEndpointsSubnetId string = vnet.properties.subnets[1].id

@description('The name of the Private Endpoints subnet')
output privateEndpointsSubnetName string = vnet.properties.subnets[1].name

@description('The resource ID of the App Service NSG')
output appServiceNsgId string = nsgAppService.id

@description('The resource ID of the Private Endpoints NSG')
output privateEndpointsNsgId string = privateEndpointsNsg.id

@description('The resource ID of the App Service Route Table')
output appServiceRouteTableId string = appServiceRouteTable.id

@description('The resource ID of the Private Endpoints Route Table')
output privateEndpointsRouteTableId string = privateEndpointsRouteTable.id
