// ==========================================
// Development Environment Parameters
// ==========================================
// This is a SAMPLE parameter file for reference only.
// GitHub Actions workflow uses environment variables instead.
//
// Required GitHub Environment Variables:
// - BICEP_BASE_NAME: Base name for resources (e.g., 'signalrchat')
// - BICEP_LOCATION: Azure region (e.g., 'eastus')
// - BICEP_VNET_ADDRESS_PREFIX: VNet CIDR (e.g., '10.0.0.0/16')
// - BICEP_APP_SERVICE_SUBNET_PREFIX: App Service subnet CIDR (e.g., '10.0.1.0/24')
// - BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX: Private Endpoints subnet CIDR (e.g., '10.0.2.0/24')
// - BICEP_ACS_DATA_LOCATION: ACS data location (e.g., 'United States')
// ==========================================

using './main.bicep'

// Sample values - replace with actual values when deploying locally
param baseName = 'signalrchat'
param environment = 'dev'
param location = 'polandcentral'
param shortLocation = 'plc'
param vnetAddressPrefix = '10.0.0.0/26'
param appServiceSubnetPrefix = '10.0.0.0/27'
param privateEndpointsSubnetPrefix = '10.0.0.32/27'
param acsDataLocation = 'Europe'
param networkingResourceGroupName = 'rg-vnet-signalrchat-dev-plc'
param otpPepper = 'LIgCUBRtfypy1JXTcTH4OXdKYrRVSjyAH4pr5rFBEKM=' // SAMPLE - replace with actual secure value

// Translation parameters
param enableTranslation = true
param translationProvider = 'LLM-GPT4oMini' // LLM-based translation with GPT-4o-mini
