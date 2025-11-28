// ==========================================
// Azure Communication Services Module
// ==========================================
// This module creates Azure Communication Services
// Note: ACS is a global resource

@description('The name of the Communication Service')
param acsName string

@description('The name of the Email Service')
param emailServiceName string

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

@description('Log Analytics Workspace ID for diagnostic logs')
param logAnalyticsWorkspaceId string = ''

// ==========================================
// Email Service (must be created first)
// ==========================================
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
  }
}

// ==========================================
// Azure Managed Domain for Email Service
// ==========================================
resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
  }
}

// ==========================================
// Azure Communication Service
// ==========================================
resource communicationService 'Microsoft.Communication/communicationServices@2025-05-01' = {
  name: acsName
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      emailDomain.id
    ]
  }
}

// ==========================================
// Diagnostic Settings
// ==========================================
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (logAnalyticsWorkspaceId != '') {
  name: 'diagnostics-${acsName}'
  scope: communicationService
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
@description('The resource ID of the Email Service')
output emailServiceId string = emailService.id

@description('The name of the Email Service')
output emailServiceName string = emailService.name

@description('The resource ID of the Email Domain')
output emailDomainId string = emailDomain.id

@description('The name of the Email Domain')
output emailDomainName string = emailDomain.name

@description('The sender email address from Azure Managed Domain')
output senderEmailAddress string = emailDomain.properties.mailFromSenderDomain

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
