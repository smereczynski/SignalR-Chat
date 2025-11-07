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
// Network Security Group for App Service Subnet
// ==========================================
resource appServiceNsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: '${vnetName}-appservice-nsg'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowAppServiceOutbound'
        properties: {
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: '*'
          access: 'Allow'
          priority: 100
          direction: 'Outbound'
        }
      }
      {
        name: 'AllowAzureLoadBalancerInbound'
        properties: {
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'AzureLoadBalancer'
          destinationAddressPrefix: '*'
          access: 'Allow'
          priority: 100
          direction: 'Inbound'
        }
      }
    ]
  }
}

// ==========================================
// Network Security Group for Private Endpoints Subnet
// ==========================================
resource privateEndpointsNsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: '${vnetName}-pe-nsg'
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
// Virtual Network with TWO Subnets
// ==========================================
resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
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
            id: appServiceNsg.id
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
output appServiceNsgId string = appServiceNsg.id

@description('The resource ID of the Private Endpoints NSG')
output privateEndpointsNsgId string = privateEndpointsNsg.id
