# Azure Deployment

This directory contains documentation for deploying SignalR Chat to Microsoft Azure.

## 📚 Documentation

- **[Bicep Templates](bicep-templates.md)** - Infrastructure as Code (IaC) using Azure Bicep
  - Resource architecture and naming conventions
  - Network configuration (VNets, subnets, private endpoints)
  - Environment specifications (dev, staging, prod)
  - Deployment procedures via GitHub Actions
  - Validation and troubleshooting

## 🚀 Quick Links

### First-Time Deployment
1. [Bootstrap Guide](../bootstrap.md) - Complete deployment from scratch
2. [GitHub Secrets Setup](../github-secrets.md) - Configure Azure credentials
3. [GitHub Variables](../github-variables.md) - Environment configuration
4. [Bicep Templates](bicep-templates.md) - Infrastructure details

### CI/CD
- [GitHub Actions](../github-actions.md) - Automated deployment workflows
- [Production Checklist](../production-checklist.md) - Pre-launch verification

### Maintenance
- [Post-Deployment Steps](../post-deployment-manual-steps.md) - Manual configuration
- [Windows to Linux Migration](../windows-to-linux-migration.md) - Platform migration guide

## 🏗️ Infrastructure Components

The Azure deployment includes:

- **Networking**: VNet with 2 subnets (App Service + Private Endpoints)
- **Compute**: App Service Plan (Linux) + Web App (.NET 10.0)
- **Database**: Cosmos DB NoSQL (messages, users, rooms)
- **Cache**: Azure Managed Redis
- **Real-time**: Azure SignalR Service
- **Communication**: Azure Communication Services (Email/SMS)
- **Translation**: Azure AI Foundry
- **Monitoring**: Log Analytics + Application Insights

## 💰 Cost Estimates

| Environment | Monthly Cost |
|-------------|--------------|
| Development | $150-250 |
| Staging | $350-500 |
| Production | $1200-1800 |

See [Bicep Templates](bicep-templates.md#environments) for detailed resource specifications.

---

**Last Updated**: 2025-12-02
