# GitHub Variables for Deployment

This guide explains how to configure GitHub environment variables for automated deployments of SignalR Chat using GitHub Actions and Bicep.

---

## 1. Required GitHub Variables (Repository Variables)

Set these in **GitHub → Settings → Secrets and variables → Actions → Variables** (repository level):

| Variable Name                        | Description                                 | Example                |
|--------------------------------------|---------------------------------------------|------------------------|
| `BICEP_BASE_NAME`                    | Base name for all resources                 | `interpres`            |
| `BICEP_LOCATION`                     | Azure region                                | `polandcentral`        |
| `BICEP_SHORT_LOCATION`               | Short location code                         | `plc`                  |
| `BICEP_VNET_ADDRESS_PREFIX`          | VNet CIDR block (/26)                       | `10.50.8.128/26`       |
| `BICEP_APP_SERVICE_SUBNET_PREFIX`    | App Service subnet (/27)                    | `10.50.8.128/27`       |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | Private Endpoints subnet (/27)            | `10.50.8.160/27`       |
| `BICEP_VNET_DNS_SERVERS`             | Custom DNS servers (comma-separated)        | `10.50.2.4`            |
| `BICEP_ACS_DATA_LOCATION`            | ACS data location                           | `Europe`               |
| `BICEP_VPN_IP`                       | VPN IP address for firewall rules (dev)     | `20.215.181.116`       |
| `ENTRA_ID_HOME_TENANT_ID`            | Home tenant ID for Entra ID                 | `6d338245-...`         |
| `ENTRA_ID_ADMIN_ROLE_VALUE`          | Admin role value for authorization          | `Admin.ReadWrite`      |

> **Note:** These are repository-level variables shared across all environments. Environment-specific values (like dev/staging/prod VNet CIDRs) should be configured in environment-scoped variables if needed.

---

## 2. Usage in Deployment

- These variables are referenced by the GitHub Actions workflow and passed as parameters to the Bicep templates.
- They control resource naming, Azure region, and network configuration for each environment.
- Do **not** store secrets (like client secrets or connection strings) as variables—use GitHub Secrets for those.

---

## 3. References

- [Deployment Guide](README.md)
- [Production Checklist](production-checklist.md)
- [GitHub Secrets Guide](github-secrets.md)

---

_Last updated: 2025-12-02_
