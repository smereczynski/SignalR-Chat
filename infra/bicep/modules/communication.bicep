// ==========================================
// Azure Communication Services Module
// ==========================================
// This module creates Azure Communication Services
// Note: ACS is a global resource

@description('The name of the Communication Service')
param acsName string

@description('The data location for the Communication Service')
@allowed([
  'Africa'
  'Asia Pacific'
  'Australia'
  'Brazil'
  'Canada'
  'Europe'
  'France'
  'Germany'
  'India'
  'Japan'
  'Korea'
  'Norway'
  'Switzerland'
  'UAE'
  'UK'
  'United States'
])
param dataLocation string = 'Europe'

@description('The Azure region for the Communication Service resource')
param location string = resourceGroup().location

// ==========================================
// Azure Communication Service
// ==========================================
resource communicationService 'Microsoft.Communication/communicationServices@2025-05-01' = {
  name: acsName
  location: location
  properties: {
    dataLocation: dataLocation
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The resource ID of the Communication Service')
output communicationServiceId string = communicationService.id

@description('The name of the Communication Service')
output communicationServiceName string = communicationService.name

@description('The connection string for the Communication Service')
@secure()
output connectionString string = communicationService.listKeys().primaryConnectionString

@description('The primary access key')
@secure()
output primaryKey string = communicationService.listKeys().primaryKey

@description('The endpoint for the Communication Service')
output endpoint string = 'https://${acsName}.communication.azure.com'
