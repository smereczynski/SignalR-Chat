# GitHub Secrets and Variables Configuration

This document describes how to configure GitHub Actions secrets and variables for the infrastructure deployment workflow.

## Overview

The deployment workflow requires secrets (sensitive data) and variables (non-sensitive configuration) to be configured per environment.

## Required GitHub Secrets

Configure these in: **Repository Settings → Secrets and variables → Actions → Secrets → New repository secret**

| Secret Name | Description | Example Value | Used In |
|-------------|-------------|---------------|---------|
| `AZURE_CLIENT_ID` | Azure AD App Registration Client ID | `12345678-1234-1234-1234-123456789abc` | Azure login |
| `AZURE_TENANT_ID` | Azure AD Tenant ID | `87654321-4321-4321-4321-cba987654321` | Azure login |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID | `abcdef01-2345-6789-abcd-ef0123456789` | Azure login |
| `OTP_PEPPER` | Cryptographic pepper for OTP hashing | `random-secure-string-here` | OTP security |
| `ENTRA_ID_CONNECTION_STRING` | Entra ID connection string (optional) | `ClientId=...;ClientSecret=...` | Entra ID auth |
| `ENTRA_ID_HOME_TENANT_ID` | **NEW** - Home tenant ID for admin authorization | `6d338245-9261-4f6d-a5a1-cd18b014a259` | Admin panel access |

### Notes on Secrets

- **ENTRA_ID_HOME_TENANT_ID**: This is the tenant ID of your organization. Only users from this tenant can access the admin panel (e.g., `/admin` endpoint).
  - Find your tenant ID: Azure Portal → Entra ID → Overview → Tenant ID
  - This prevents users from other tenants (in multi-tenant scenarios) from accessing admin features

- **ENTRA_ID_CONNECTION_STRING**: Format is `ClientId=<app-id>;ClientSecret=<secret>;TenantId=<tenant-id>` (optional, only needed if using Entra ID)

## Required GitHub Variables

Configure these in: **Repository Settings → Secrets and variables → Actions → Variables → New repository variable**

| Variable Name | Description | Example Value (dev) | Used In |
|---------------|-------------|---------------------|---------|
| `BICEP_BASE_NAME` | Base name for resources | `signalrchat` | Resource naming |
| `BICEP_LOCATION` | Azure region full name | `westeurope` | Resource location |
| `BICEP_SHORT_LOCATION` | Azure region short code | `weu` | Resource naming |
| `BICEP_VNET_ADDRESS_PREFIX` | VNet address space | `10.0.0.0/16` | Networking |
| `BICEP_APP_SERVICE_SUBNET_PREFIX` | App Service subnet | `10.0.1.0/24` | Networking |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | Private endpoints subnet | `10.0.2.0/24` | Networking |
| `BICEP_ACS_DATA_LOCATION` | ACS data location | `europe` | ACS configuration |
| `BICEP_VNET_DNS_SERVERS` | Custom DNS servers (optional) | `10.0.0.4,10.0.0.5` | Networking |
| `ENTRA_ID_ADMIN_ROLE_VALUE` | **NEW** - App Role value for admin access | `Admin.ReadWrite` | Admin panel access |

### Notes on Variables

- **ENTRA_ID_ADMIN_ROLE_VALUE**: This is the name of the App Role in your Entra ID App Registration that grants admin access.
  - Default value: `Admin.ReadWrite` (as per project convention)
  - To find/configure: Azure Portal → Entra ID → App registrations → Your app → App roles
  - Users must be assigned this role to access admin features (e.g., `/admin` endpoint)
  - Example role values: `Admin.ReadWrite`, `Admin.FullAccess`, `ChatAdmin`

## Environment-Specific Configuration

GitHub Actions environments allow different values per deployment stage. Configure these environments:

### 1. Create Environments

Go to: **Repository Settings → Environments**

Create three environments:
- `dev`
- `staging`
- `prod`

### 2. Configure Per-Environment Values

For each environment, you can override specific variables:

#### Example: Development Environment

Variables (example):
```
BICEP_BASE_NAME = signalrchat
BICEP_LOCATION = westeurope
BICEP_SHORT_LOCATION = weu
BICEP_VNET_ADDRESS_PREFIX = 10.0.0.0/16
BICEP_APP_SERVICE_SUBNET_PREFIX = 10.0.1.0/24
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX = 10.0.2.0/24
BICEP_ACS_DATA_LOCATION = europe
ENTRA_ID_ADMIN_ROLE_VALUE = Admin.ReadWrite
```

Secrets (example):
```
ENTRA_ID_HOME_TENANT_ID = 6d338245-9261-4f6d-a5a1-cd18b014a259
```

#### Example: Production Environment

Variables might differ for networking:
```
BICEP_VNET_ADDRESS_PREFIX = 10.10.0.0/16
BICEP_APP_SERVICE_SUBNET_PREFIX = 10.10.1.0/24
BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX = 10.10.2.0/24
```

## Setup Instructions

### Step 1: Configure Secrets

1. Go to **Repository Settings → Secrets and variables → Actions**
2. Click **Secrets** tab
3. Click **New repository secret**
4. Add each secret from the table above:
   - Name: `ENTRA_ID_HOME_TENANT_ID`
   - Secret: Your tenant ID (e.g., `6d338245-9261-4f6d-a5a1-cd18b014a259`)
   - Click **Add secret**

### Step 2: Configure Variables

1. Go to **Repository Settings → Secrets and variables → Actions**
2. Click **Variables** tab
3. Click **New repository variable**
4. Add each variable from the table above:
   - Name: `ENTRA_ID_ADMIN_ROLE_VALUE`
   - Value: `Admin.ReadWrite` (or your custom role name)
   - Click **Add variable**

### Step 3: Verify Configuration

1. Go to **Actions** tab
2. Select **Deploy Infrastructure** workflow
3. Click **Run workflow**
4. Select environment: `dev`
5. Select action: `validate`
6. Click **Run workflow**

If configuration is correct, the validation step should show all variables are present.

## Admin Authorization Flow

When both `ENTRA_ID_HOME_TENANT_ID` and `ENTRA_ID_ADMIN_ROLE_VALUE` are configured:

1. **Home Tenant Check**: User must be from the specified tenant ID
   - Validated by `HomeTenantHandler` middleware
   - Checks `http://schemas.microsoft.com/identity/claims/tenantid` claim

2. **App Role Check**: User must have the specified role assigned
   - Validated by `[Authorize(Roles = "...")]` attribute
   - Checks `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` claim

3. **Both Conditions Required**: User must pass BOTH checks to access admin endpoints

### Example: Admin Panel Access

```csharp
[Authorize(Roles = "Admin.ReadWrite")]  // Requires app role
[RequireHomeTenant]                      // Requires home tenant
public class AdminController : Controller
{
    // Only accessible to users with Admin.ReadWrite role from home tenant
}
```

## Security Best Practices

1. **Rotate Secrets Regularly**: Update secrets every 90 days
2. **Use Service Principal**: For Azure login, use a dedicated service principal with minimal permissions
3. **Restrict Tenant Access**: Set `ENTRA_ID_HOME_TENANT_ID` to your organization's tenant only
4. **Custom Role Names**: Consider using organization-specific role names (e.g., `MyOrg.Admin`)
5. **Environment Protection**: Enable required reviewers for `prod` environment in GitHub

## Troubleshooting

### "Context access might be invalid" Warning

If you see this warning in GitHub Actions:
- This is a **linting warning** only - the workflow will still run
- GitHub can't validate variable names until they're actually created
- Once you create the variable, the warning can be ignored

### Missing Variables Error

If deployment fails with "Missing required environment variables":
1. Check that all variables are created in the **Variables** tab
2. Ensure variable names match exactly (case-sensitive)
3. Verify you're deploying to the correct environment

### Admin Access Denied

If users can't access admin panel despite having the role:
1. Verify `ENTRA_ID_HOME_TENANT_ID` matches user's tenant
2. Verify role name matches `ENTRA_ID_ADMIN_ROLE_VALUE`
3. Check app logs for authorization failures
4. Verify user has role assigned in Entra ID

## References

- [GitHub Actions: Encrypted secrets](https://docs.github.com/en/actions/security-guides/encrypted-secrets)
- [GitHub Actions: Variables](https://docs.github.com/en/actions/learn-github-actions/variables)
- [Azure: Service principals](https://learn.microsoft.com/azure/active-directory/develop/app-objects-and-service-principals)
- [Entra ID: App roles](https://learn.microsoft.com/azure/active-directory/develop/howto-add-app-roles-in-azure-ad-apps)
