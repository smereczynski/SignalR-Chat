# GitHub Variables for Deployment

This guide explains how to configure GitHub environment variables for automated deployments of SignalR Chat using GitHub Actions and Bicep.

---

## 1. Required GitHub Variables (Environment Variables)

Set these in **GitHub → Settings → Environments → [dev/staging/prod] → Environment variables** for each environment:

| Variable Name                        | Description                                 | Example (dev)         | Example (staging)      | Example (prod)         |
|--------------------------------------|---------------------------------------------|-----------------------|------------------------|------------------------|
| `BICEP_BASE_NAME`                    | Base name for all resources                 | `signalrchat`         | `signalrchat`          | `signalrchat`          |
| `BICEP_LOCATION`                     | Azure region                                | `polandcentral`       | `polandcentral`        | `polandcentral`        |
| `BICEP_VNET_ADDRESS_PREFIX`          | VNet CIDR block (/26)                       | `10.0.0.0/26`         | `10.1.0.0/26`          | `10.2.0.0/26`          |
| `BICEP_APP_SERVICE_SUBNET_PREFIX`    | App Service subnet (/27)                    | `10.0.0.0/27`         | `10.1.0.0/27`          | `10.2.0.0/27`          |
| `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX` | Private Endpoints subnet (/27)            | `10.0.0.32/27`        | `10.1.0.32/27`         | `10.2.0.32/27`         |
| `BICEP_ACS_DATA_LOCATION`            | ACS data location                           | `Europe`              | `Europe`               | `Europe`               |

> **Note:** Each environment (dev, staging, prod) must have unique VNet address spaces to avoid conflicts.

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

_Last updated: 2025-11-19_
