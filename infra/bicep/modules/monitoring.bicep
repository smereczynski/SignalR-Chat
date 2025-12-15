// ==========================================
// Monitoring Module - Log Analytics + Application Insights
// ==========================================
// This module creates monitoring infrastructure:
// - Log Analytics Workspace
// - Application Insights (workspace-based)

@description('The base name for monitoring resources')
param baseName string

@description('The location for all resources')
param location string = resourceGroup().location

@description('Short location abbreviation for resource names (e.g., plc for polandcentral)')
param shortLocation string

@description('The environment (dev, staging, prod) - determines retention')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

// ==========================================
// Variables
// ==========================================
var retentionInDays = environment == 'prod' ? 365 : (environment == 'staging' ? 90 : 30)
var logAnalyticsName = 'law-${baseName}-${environment}-${shortLocation}'
var appInsightsName = 'ai-${baseName}-${environment}-${shortLocation}'

// ==========================================
// Log Analytics Workspace
// ==========================================
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: 10
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ==========================================
// Application Insights
// ==========================================
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: retentionInDays
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ==========================================
// Outputs
// ==========================================
@description('The resource ID of the Log Analytics Workspace')
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id

@description('The name of the Log Analytics Workspace')
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name

@description('The resource ID of the Application Insights')
output applicationInsightsId string = appInsights.id

@description('The name of the Application Insights')
output applicationInsightsName string = appInsights.name

@description('The instrumentation key for Application Insights')
@secure()
output instrumentationKey string = appInsights.properties.InstrumentationKey

@description('The connection string for Application Insights')
@secure()
output connectionString string = appInsights.properties.ConnectionString
