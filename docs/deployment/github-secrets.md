# GitHub Secrets and Entra ID Connection String

This guide explains how to configure GitHub secrets and build the Entra ID connection string for secure, automated deployments of SignalR Chat.

---

## 1. Required GitHub Secrets for Deployment

Set these in **GitHub → Settings → Secrets and variables → Actions → Secrets** (repository level):

| Secret Name                  | Description                                 | How to Generate/Find                |
|------------------------------|---------------------------------------------|-------------------------------------|
| `AZURE_CLIENT_ID`            | Azure Service Principal Client ID           | Azure Portal / CLI                  |
| `AZURE_TENANT_ID`            | Azure AD Tenant ID                          | Azure Portal / CLI                  |
| `AZURE_SUBSCRIPTION_ID`      | Azure Subscription ID                       | Azure Portal / CLI                  |
| `ENTRA_ID_CONNECTION_STRING` | Entra ID connection string (see below)      | Build as described below            |
| `OTP_PEPPER`                 | OTP hashing pepper (Base64, 32 bytes)       | `openssl rand -base64 32`           |

> **Note:** These are repository-level secrets shared across all environments.

---

## 2. How to Build the Entra ID Connection String

The Entra ID connection string is used for secure authentication and must be stored as a single secret.

**Format:**
```
ClientId=<your-client-id>;ClientSecret=<your-client-secret>;TenantId=<your-tenant-id>
```

**Example:**
```
ClientId=12345678-aaaa-bbbb-cccc-1234567890ab;ClientSecret=your-very-long-secret;TenantId=your-tenant-guid
```

**Steps:**
1. Register an app in Azure Entra ID (Azure AD) and generate a client secret.
2. Copy the Application (client) ID, Directory (tenant) ID, and the client secret value.
3. Construct the connection string as above.
4. Add it as the `ENTRA_ID_CONNECTION_STRING` secret in GitHub for each environment.

---

## 3. Usage in Deployment

- The Bicep templates and GitHub Actions workflows are designed to use only the `ENTRA_ID_CONNECTION_STRING` for Entra ID authentication.
- Do **not** set or reference `ClientId` or `ClientSecret` individually in app settings, Bicep parameters, or GitHub secrets.

---

## 4. References

- [Configuration Guide](../getting-started/configuration.md)
- [Authentication Guide](../features/authentication.md)
- [Production Checklist](production-checklist.md)

---

_Last updated: 2025-12-02_
