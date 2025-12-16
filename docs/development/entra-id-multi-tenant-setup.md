# Entra ID Multi-Tenant Setup Guide

This document explains how to configure Microsoft Entra ID (formerly Azure Active Directory) for multi-tenant authentication in SignalR Chat. It covers the responsibilities of both the **Application Owner (Home Tenant)** and **External Tenant Administrators**.

---

## Overview

SignalR Chat uses a **multi-tenant authentication model** that allows users from multiple organizations to authenticate using their existing Microsoft Entra ID credentials. This requires:

1. **Home Tenant (Application Owner)**: Registers and maintains the multi-tenant application
2. **External Tenants (Customer Organizations)**: Grant admin consent to allow their users to authenticate

---

## Architecture: Multi-Tenant UPN-Based Authorization

### Authentication Flow

```
User (External Tenant) 
  → Signs in with Microsoft
  → Token issued by External Tenant (not Home Tenant)
  → Token contains:
      - User claims (name, email, UPN)
      - Tenant ID (tid) - External Tenant ID
  → Application validates:
      ✓ Token signature and expiration
      ✓ Token issuer (Microsoft Identity Platform)
      ✓ User is NOT MSA (consumer account) - explicitly denied
      ✓ Tenant ID in AllowedTenants list (optional security layer)
      ✓ User UPN exists in application database (MANDATORY)
  → Access granted ✅
```

For the end-to-end application login UX (cookie session, optional silent SSO, OTP fallback, redirects like `/login?reason=...`), see:

- [Authentication](../features/authentication.md)

Note: automatic silent SSO is **optional** and disabled by default via `EntraId:AutomaticSso:Enable`.

### Key Concept: UPN-Based Authorization

⚠️ **CRITICAL**: Authorization is managed **entirely in-application** using the User Principal Name (UPN) claim from the token. The application does NOT use:
- ❌ External group claims or group membership
- ❌ App roles assigned in Entra ID
- ❌ Directory roles or administrative units

✅ **Authorization model**: User's UPN must exist in the application's database **before** their first login. No auto-provisioning.

| Entity | Role | Responsibilities |
|--------|------|------------------|
| **Home Tenant** | Application owner | Registers app, manages client secret, configures redirect URIs |
| **External Tenant** | Customer organization | Grants admin consent (or users consent individually) |
| **Application Database** | Authorization store | Stores authorized UPNs - **MUST be pre-populated** |
| **Token Issuer** | Microsoft Identity Platform | Issues tokens on behalf of External Tenant |
| **Application Logic** | Authorization enforcer | Validates UPN exists in database, blocks MSA accounts |

**Example:**

- **Home Tenant**: Contoso (owns SignalR Chat application)
- **External Tenant**: Fabrikam (customer organization)
- **User**: alice@fabrikam.com
- **Token**: Issued by Fabrikam tenant, contains UPN `alice@fabrikam.com`
- **Validation**: Application checks if `alice@fabrikam.com` exists in authorized users database

---

## Part 1: Home Tenant Setup (Application Owner)

The application owner (typically the SignalR Chat deployment team) performs these steps **once** in their home tenant.

### Prerequisites

- Azure subscription
- Entra ID tenant (home tenant)
- Global Administrator or Application Administrator role

### Step 1: Register Multi-Tenant Application

1. **Navigate to Azure Portal**
   ```
   https://portal.azure.com
   → Microsoft Entra ID
   → App registrations
   → New registration
   ```

2. **Configure Application Registration**
   
   | Field | Value | Notes |
   |-------|-------|-------|
   | **Name** | `SignalR Chat` | Application display name |
   | **Supported account types** | **Accounts in any organizational directory (Any Microsoft Entra ID tenant - Multitenant)** | ⚠️ Critical for multi-tenant |
   | **Redirect URI** | Platform: `Web`<br>URI: `https://signalrchat-prod-plc.azurewebsites.net/signin-oidc` | Production URL |

   Click **Register**.

3. **Add Additional Redirect URIs**
   
   After registration, go to **Authentication** and add:
   
   | Environment | Redirect URI |
   |-------------|-------------|
   | Development | `https://localhost:5099/signin-oidc` |
   | Staging | `https://signalrchat-staging-plc.azurewebsites.net/signin-oidc` |
   | Production | `https://signalrchat-prod-plc.azurewebsites.net/signin-oidc` |

4. **Configure Logout URLs**
   
   In **Authentication** → **Front-channel logout URL**:
   - `https://signalrchat-prod-plc.azurewebsites.net/signout-oidc`

5. **Enable Token Types**
   
   In **Authentication** → **Implicit grant and hybrid flows**:
   - ✅ **ID tokens** (required for OpenID Connect)
   - ☑️ **Access tokens** (optional, not needed for this implementation)

### Step 2: Configure Token Claims (Optional)

The application uses standard claims (name, email, UPN) which are included by default in ID tokens. No additional token configuration is required for basic authentication.

**Optional**: For enhanced consistency, you can add optional claims:

1. **Navigate to Token Configuration**
   ```
   App registration → Token configuration → Add optional claim
   ```

2. **Add Preferred Username Claim** (recommended for UPN consistency)
   - Token type: **ID**
   - Claim: **preferred_username** (contains UPN)
   - Click **Add**

3. **Add Email Claim** (if not included by default)
   - Token type: **ID**
   - Claim: **email**
   - Click **Add**

This ensures the application receives `preferred_username` claim which contains the user's UPN (e.g., `alice@fabrikam.com`).

⚠️ **Note**: The application does NOT use group claims, app roles, or directory roles for authorization. Authorization is UPN-based only.

### Step 3: Configure API Permissions

1. **Navigate to API Permissions**
   ```
   App registration → API permissions
   ```

2. **Required Permissions**
   
   | API | Permission | Type | Admin Consent Required | Purpose |
   |-----|-----------|------|----------------------|---------|
   | Microsoft Graph | `User.Read` | Delegated | No | Read user's basic profile (name, email, UPN) |

   **Note**: No additional permissions are required since authorization is managed in-application using UPN.

3. **Verify User.Read Permission**
   
   This permission is added by default when creating the app registration. Verify it exists:
   ```
   API permissions → Microsoft Graph → User.Read (Delegated)
   ```

4. **Grant Admin Consent (Home Tenant)** - Optional
   ```
   → Grant admin consent for [Home Tenant Name]
   → Yes
   ```
   
   ⚠️ **Note**: Admin consent is not required for `User.Read` (user-level permission). This step is optional for the home tenant. External tenants will grant consent during first sign-in or via admin consent URL.

### Step 4: Create Client Secret

1. **Navigate to Certificates & Secrets**
   ```
   App registration → Certificates & secrets → Client secrets
   ```

2. **Create New Secret**
   - Description: `SignalR Chat Production Secret`
   - Expires: **24 months** (maximum allowed)
   - Click **Add**

3. **Copy Secret Value**
   
   ⚠️ **CRITICAL**: Copy the secret **value** (not the Secret ID) immediately - it will never be shown again.
   
   ```
   Example: abc123XYZ~mno456PQR-stu789
   ```

4. **Store Secret Securely**
   
   - **Recommended**: Azure Key Vault
     ```bash
     az keyvault secret set \
       --vault-name "kv-signalrchat-prod" \
       --name "EntraId--ClientSecret" \
       --value "abc123XYZ~mno456PQR-stu789"
     ```
   
   - **Development**: `.env.local` (gitignored)
     ```bash
     ENTRA_CLIENT_SECRET=abc123XYZ~mno456PQR-stu789
     ```

### Step 5: Note Application IDs

1. **Copy Application (Client) ID**
   ```
   App registration → Overview → Application (client) ID
   Example: 12345678-1234-1234-1234-123456789abc
   ```

2. **Copy Directory (Tenant) ID** (Home Tenant ID)
   ```
   App registration → Overview → Directory (tenant) ID
   Example: abcdef12-3456-7890-abcd-ef1234567890
   ```

3. **Configuration Summary**
   
   Store these values for application configuration:
   
   | Setting | Value | Where to Store |
   |---------|-------|---------------|
   | `EntraId:ClientId` | Application (client) ID | App settings / Key Vault |
   | `EntraId:ClientSecret` | Secret value from Step 4 | **Key Vault only** |
   | `EntraId:TenantId` | `"organizations"` | App settings (not home tenant ID) |
   | `EntraId:Instance` | `https://login.microsoftonline.com/` | App settings |

   ⚠️ **Important**: Use `"organizations"` for `TenantId`, NOT your home tenant ID. This enables multi-tenant authentication.

### Step 6: Generate Admin Consent URL

For each external tenant (customer organization), generate an admin consent URL:

```
https://login.microsoftonline.com/organizations/adminconsent
  ?client_id={APPLICATION_CLIENT_ID}
  &redirect_uri={REDIRECT_URI}
  &state={OPTIONAL_STATE}
```

**Example**:
```
https://login.microsoftonline.com/organizations/adminconsent?client_id=12345678-1234-1234-1234-123456789abc&redirect_uri=https://signalrchat-prod-plc.azurewebsites.net/signin-oidc
```

**Provide this URL to external tenant administrators** (see Part 2).

---

## Part 2: External Tenant Setup (Customer Organization)

Each customer organization (external tenant) must perform these steps to enable their users to access SignalR Chat.

### Prerequisites

- Microsoft Entra ID tenant (customer organization)
- Global Administrator or Privileged Role Administrator role
- Admin consent URL from Home Tenant (Part 1, Step 6)

### Step 1: Grant Admin Consent

1. **Receive Admin Consent URL**
   
   The application owner provides a URL like:
   ```
   https://login.microsoftonline.com/organizations/adminconsent?client_id=12345678-1234-1234-1234-123456789abc&redirect_uri=https://signalrchat-prod-plc.azurewebsites.net/signin-oidc
   ```

2. **Navigate to URL as Global Administrator**
   
   - Open URL in browser
   - Sign in with Global Admin or Privileged Role Admin credentials
   - Review requested permissions:
     - ✅ Sign you in and read your profile (`User.Read`)

3. **Grant Consent**
   
   - Click **Accept**
   - Confirms that users from your organization can authenticate to SignalR Chat

4. **Verify Consent (Optional)**
   
   ```
   Azure Portal → Microsoft Entra ID → Enterprise applications
   → All applications
   → Search "SignalR Chat"
   → Permissions → Review granted permissions
   ```

### Step 2: Provide User UPNs to Application Owner

Send the following information to the SignalR Chat application owner for each user who should have access:

| Information | Value | Example |
|------------|-------|---------|
| **Organization Name** | Your company name | Fabrikam Inc. |
| **Tenant ID** (optional) | Directory (tenant) ID | `fabrikam.onmicrosoft.com` or GUID |
| **User UPNs** | List of user principal names | `alice@fabrikam.com`, `bob@fabrikam.com` |
| **User Display Names** | Full names (optional) | Alice Smith, Bob Jones |
| **Admin Contact** | Email for troubleshooting | `admin@fabrikam.com` |

The application owner will add these UPNs to the application's authorized users database.

**Alternative**: Application owner can provide a self-service portal where external tenant admins can manage authorized users.

---

## Part 3: Application Configuration (Home Tenant)

After receiving user UPNs from external tenants, the application owner adds them to the authorized users database.

### ⚠️ CRITICAL: Pre-Population Required

**Before a user can log in via Entra ID**, their UPN **MUST** be pre-populated in the database. The application uses **strict UPN matching** with no auto-provisioning or fallback to email/username.

**Authorization Flow:**
1. User logs in with Microsoft → Token contains UPN (e.g., `alice@fabrikam.com`)
2. Application queries: `SELECT * FROM users WHERE upn = 'alice@fabrikam.com'`
3. **If match found** → Update user profile (TenantId, DisplayName, Email) → Grant access ✅
4. **If NO match** → Deny access (redirect to OTP if `OtpForUnauthorizedUsers: true`) ❌

**Admin must pre-populate UPN:**
```sql
-- Example: Update existing user with UPN before first Entra ID login
UPDATE users SET upn = 'alice@fabrikam.com' WHERE username = 'alice';
```

### Configuration Structure

```json
{
  "EntraId": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "organizations",
    "ClientId": "12345678-1234-1234-1234-123456789abc",
    "ClientSecret": "${ENTRA_CLIENT_SECRET}",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-oidc",
    
    "Authorization": {
      "AllowedTenants": [
        "fabrikam.onmicrosoft.com",
        "contoso.onmicrosoft.com",
        "northwind.onmicrosoft.com"
      ],
      "RequireTenantValidation": true
    },
    
    "Fallback": {
      "EnableOtp": true,
      "OtpForUnauthorizedUsers": false
    },
    
    "AutomaticSso": {
      "Enable": false,
      "AttemptOncePerSession": true,
      "AttemptCookieName": "sso_attempted"
    }
  }
}
```

### User Database Management

Authorization is managed via the application's user database (Cosmos DB). For each authorized user, **the `upn` field MUST be set** before their first Entra ID login.

**Users Container** (`users`):
```json
{
  "id": "alice",
  "userName": "alice",
  "fullName": "Alice Johnson",
  "email": "alice@fabrikam.com",
  "mobileNumber": "+1234567890",
  "upn": "alice@fabrikam.com",       // ← MUST be set before Entra ID login
  "displayName": "Alice Smith",       // ← Auto-updated on login
  "tenantId": "abc-123-def-456",      // ← Auto-updated on login
  "enabled": true,
  "fixedRooms": ["general"],
  "defaultRoom": "general"
}
```

**Adding Users** (Pre-Population Methods):

1. **Update Existing User** (Recommended for seeded users):
   ```bash
   # Via Azure Data Explorer or Cosmos DB SDK
   # Patch existing user to add UPN field
   PATCH /dbs/chat/colls/users/docs/alice
   {
     "upn": "alice@fabrikam.com"
   }
   ```

2. **Manual Entry** (Development/Testing):
   ```bash
   # Via Cosmos DB Data Explorer
   # Create new user document with upn field populated
   ```

3. **Bulk Import Script** (Production - Multiple Users):
   ```bash
   # Example: Update multiple users with UPNs
   # users.csv:
   # username,upn
   # alice,alice@fabrikam.com
   # bob,bob@fabrikam.com
   # charlie,charlie@contoso.com
   
   # Run bulk update script
   dotnet run --project tools/Chat.DataSeed -- \
     --update-upns users.csv
   ```

4. **REST API** (Future Enhancement - Admin Portal):
   ```http
   PATCH /api/admin/users/alice
   Authorization: Bearer <admin-token>
   Content-Type: application/json
   
   {
     "upn": "alice@fabrikam.com"
   }
   ```

**⚠️ Important Notes:**
- Users with `upn: null` or `upn: ""` **CANNOT** log in via Entra ID
- UPN must exactly match the `preferred_username` claim from the Entra ID token
- UPN is case-insensitive (e.g., `Alice@Fabrikam.com` matches `alice@fabrikam.com`)
- **Microsoft consumer accounts (MSA)** are explicitly **BLOCKED** - only organizational accounts allowed
- OTP authentication still works for users without UPN (if `EnableOtp: true`)

**Configuration Options:**

| Setting | Description | Default |
|---------|-------------|---------|   | `Authorization.AllowedTenants` | List of allowed tenant domains/IDs (empty = allow any tenant) | `[]` |
| `Authorization.RequireTenantValidation` | Enforce AllowedTenants list | `true` |
| `Fallback.EnableOtp` | Allow OTP as alternative authentication | `true` |
| `Fallback.OtpForUnauthorizedUsers` | Allow OTP fallback for users denied by Entra ID | `false` |
| `AutomaticSso.Enable` | Attempt silent SSO on first visit to / or /chat | `false` |
| `AutomaticSso.AttemptOncePerSession` | Only try automatic SSO once per browser session | `true` |

### Azure App Service Configuration

For production, store configuration in Azure App Service (secure):

```bash
az webapp config appsettings set \
  --name signalrchat-prod-plc \
  --resource-group rg-signalrchat-prod-plc \
  --settings \
    "EntraId__ClientId=12345678-1234-1234-1234-123456789abc" \
    "EntraId__TenantId=organizations" \
    "EntraId__Authorization__AllowedTenants__0=fabrikam.onmicrosoft.com" \
    "EntraId__Authorization__AllowedTenants__1=contoso.onmicrosoft.com" \
    "EntraId__Authorization__RequireTenantValidation=true" \
    "EntraId__Fallback__EnableOtp=true" \
    "EntraId__Fallback__OtpForUnauthorizedUsers=false" \
    "EntraId__AutomaticSso__Enable=false"
```

Store secret in Azure Key Vault:

```bash
az keyvault secret set \
  --vault-name kv-signalrchat-prod \
  --name "EntraId--ClientSecret" \
  --value "abc123XYZ~mno456PQR-stu789"
```

---

## Token Claims Reference

Understanding token claims helps troubleshoot authentication issues.

### ID Token Claims (After Successful Authentication)

```json
{
  "aud": "12345678-1234-1234-1234-123456789abc",  // Application client ID
  "iss": "https://login.microsoftonline.com/{external-tenant-id}/v2.0",  // External tenant issuer
  "iat": 1700000000,
  "nbf": 1700000000,
  "exp": 1700003600,
  "name": "Alice Smith",
  "preferred_username": "alice@fabrikam.com",  // UPN - used for authorization
  "email": "alice@fabrikam.com",
  "oid": "99999999-8888-7777-6666-555555555555",  // User object ID (in external tenant)
  "sub": "AAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
  "tid": "fabrikam-tenant-id-guid",  // External tenant ID (NOT home tenant ID)
  "ver": "2.0"
}
```

### Key Claims for Authorization

| Claim | Description | Validation |
|-------|-------------|-----------|
| `preferred_username` | User Principal Name (UPN) | Must exist in application's users database |
| `tid` | Tenant ID of user's organization | Optional: Can be in `AllowedTenants` list for extra security |
| `aud` | Application client ID | Must match registered application ID |
| `iss` | Token issuer | Must be Microsoft login endpoint |
| `email` | User email address | Fallback if `preferred_username` not present |

---

## Onboarding New Customer Organizations

### Quick Reference Checklist

**External Tenant Admin Tasks:**
1. ☐ Receive admin consent URL from application owner
2. ☐ Grant admin consent as Global Administrator (or have users consent individually)
3. ☐ Compile list of authorized user UPNs (User Principal Names)
4. ☐ Provide user UPNs to application owner

**Application Owner Tasks:**
1. ☐ Receive user UPNs from external tenant admin
2. ☐ Add user entries to application database (Cosmos DB `users` container)
3. ☐ (Optional) Add tenant domain to `EntraId:Authorization:AllowedTenants` for extra security
4. ☐ Verify test user can authenticate

**Timeline**: ~10-15 minutes per customer organization

---

## Security Considerations

### Token Validation

The application validates:

1. **Issuer**: Token must be from Microsoft Identity Platform
2. **Audience**: Token must be issued for this application (client ID match)
3. **Signature**: Token must be signed by Microsoft signing keys
4. **Expiration**: Token must not be expired
5. **Tenant** (optional): Token's `tid` can be validated against allowed tenants list
6. **UPN**: User's UPN (from `preferred_username` or `email` claim) must exist in application database

### UPN Security

- UPN cannot be spoofed (validated by Microsoft in token)
- UPN is persistent across user's organizational lifetime
- UPN is unique within a tenant
- Application database stores authorized UPNs
- User cannot authenticate if UPN not in database (even with valid Entra ID token)

### Multi-Tenant Isolation

- Each tenant's UPNs are scoped to their domain (e.g., `@fabrikam.com`)
- User from Tenant A cannot impersonate user from Tenant B
- Application can optionally validate tenant ID matches UPN domain
- Authorization is entirely in-application (no external dependencies)

---

## Troubleshooting

### Issue: "Your organization is not authorized to access this application"

**Cause**: User's tenant ID (`tid`) is not in `AllowedTenants` list AND `RequireTenantValidation: true`.

**Solution**:
1. Verify tenant domain or ID in user's token (check `tid` claim in logs)
2. Add tenant to configuration:
   ```json
   "AllowedTenants": ["fabrikam.onmicrosoft.com", "contoso.onmicrosoft.com"]
   ```
3. Deploy configuration update
4. **Or** set `RequireTenantValidation: false` to skip tenant validation (authorization via UPN only)

### Issue: "Microsoft consumer accounts are not supported"

**Cause**: User attempted to sign in with a personal Microsoft account (e.g., @outlook.com, @hotmail.com).

**Solution**:
This is **by design**. The application only accepts organizational accounts (Entra ID). Users must:
- Use their work/school account from an organization
- Contact their IT admin to provision an organizational account
- Use OTP authentication if enabled (`EnableOtp: true`)

### Issue: "You are not authorized to access this application. Please contact your administrator."

**Cause**: User's UPN is not in the application's authorized users database.

**Solution (Application Owner)**:
1. Verify user's UPN (from `preferred_username` or `email` claim in token)
2. Check if UPN exists in Cosmos DB `users` container:
   ```bash
   # Via Azure Portal → Cosmos DB → Data Explorer
   # Query: SELECT * FROM c WHERE c.upn = "alice@fabrikam.com"
   ```
3. Add user to database if missing
4. Verify user's `isActive` flag is `true`

**Solution (External Tenant Admin)**:
1. Verify user's UPN was provided to application owner
2. Contact application owner to confirm user was added to database
3. Have user sign out and sign in again

### Issue: User sees "AADSTS65001: The user or administrator has not consented"

**Cause**: External tenant admin has not granted consent to the application (or user has not consented individually).

**Solution**:
1. External tenant admin must grant consent using admin consent URL, OR
2. User can consent individually on first sign-in (if `User.Read` is the only permission)
3. Navigate to provided URL as Global Administrator
4. Click "Accept" to grant permissions

### Issue: Token does not contain `preferred_username` claim

**Cause**: Optional claim not configured, or using older token version.

**Solution**:
1. Check if token contains `email` claim (use as fallback for UPN)
2. Add `preferred_username` optional claim in app registration:
   ```
   App registration → Token configuration → Add optional claim → preferred_username
   ```
3. Have user sign out and sign in again to get fresh token

---

## Advanced Scenarios

### Bulk User Import

For onboarding large organizations, use bulk import:

**CSV Format** (`authorized_users.csv`):
```csv
upn,displayName,email,tenantId
alice@fabrikam.com,Alice Smith,alice@fabrikam.com,fabrikam-tenant-id
bob@fabrikam.com,Bob Jones,bob@fabrikam.com,fabrikam-tenant-id
```

**Import Script**:
```bash
dotnet run --project tools/Chat.DataSeed -- \
  --import-users authorized_users.csv
```

### User Deactivation

To revoke access without deleting user data:

1. Set `isActive: false` in user document
2. User's authentication will be rejected
3. Chat history preserved for audit trail

**Example**:
```json
{
  "id": "alice@fabrikam.com",
  "upn": "alice@fabrikam.com",
  "isActive": false,  // Revokes access
  "deactivatedAt": "2025-12-01T00:00:00Z",
  "deactivatedBy": "admin@contoso.com"
}
```

### Tenant-Level Access Control

For strict security, explicitly allow tenants:

```json
"AllowedTenants": [
  "contoso.onmicrosoft.com",
  "fabrikam.onmicrosoft.com"
]
```

Any tenant not in list → authentication rejected (even if user UPN is in database).

**Use case**: Contract-based access (only allow customers with active subscriptions).

### Role-Based Permissions (Future)

Current implementation: Binary access (authenticated/not authenticated)

Future enhancement: Add role-based permissions:

```json
{
  "id": "alice@fabrikam.com",
  "upn": "alice@fabrikam.com",
  "roles": ["chat-user", "moderator"],
  "permissions": ["read-messages", "send-messages", "delete-messages"]
}
```

Application checks user's roles/permissions for fine-grained authorization.

---

## References

- [Microsoft Entra ID Multi-Tenant Apps](https://learn.microsoft.com/entra/identity-platform/howto-convert-app-to-be-multi-tenant)
- [Admin Consent Workflow](https://learn.microsoft.com/entra/identity-platform/v2-admin-consent)
- [Token Reference (ID Tokens)](https://learn.microsoft.com/entra/identity-platform/id-tokens)
- [Optional Claims](https://learn.microsoft.com/entra/identity-platform/optional-claims)
- [Security Best Practices](https://learn.microsoft.com/entra/identity-platform/security-best-practices-for-app-registration)
- [UPN Claim Documentation](https://learn.microsoft.com/entra/identity-platform/id-token-claims-reference#preferred_username)

---

## Summary

### Home Tenant Responsibilities (One-Time Setup)
1. Register multi-tenant application
2. Configure API permissions (`User.Read` only)
3. Create client secret and store securely
4. Generate admin consent URLs for external tenants
5. Set up application database for authorized users

### External Tenant Responsibilities (Per Organization)
1. Grant admin consent to application (or allow users to consent individually)
2. Compile list of authorized user UPNs
3. Provide user UPNs to application owner
4. Optionally provide tenant ID for `AllowedTenants` validation

### Application Owner Responsibilities (Per Onboarding)
1. Receive user UPNs from external tenant admin
2. Add users to application database with `upn` field populated
3. (Optional) Add tenant domain/ID to `AllowedTenants` configuration
4. Verify test user can authenticate

**Result**: Users from external tenants can authenticate with their organizational credentials, and access is controlled by UPN presence in the application database.
