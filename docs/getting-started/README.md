# Getting Started

Welcome! This section will help you get SignalR Chat up and running, whether you want to try it locally or deploy to Azure.

## Quick Navigation

### 🚀 5-Minute Quickstart
Want to try it right now without any setup?

➡️ **[Quickstart Guide](quickstart.md)** - Run locally in 5 minutes (no Azure required)

### 📦 Full Installation
Ready to set up with Azure resources for persistence and scale?

➡️ **[Installation Guide](installation.md)** - Complete setup with Cosmos DB, Redis, and Azure

### ⚙️ Configuration
Need help with environment variables and configuration options?

➡️ **[Configuration Reference](configuration.md)** - All environment variables and settings

## What's the Difference?

| Aspect | Quickstart | Full Installation |
|--------|-----------|-------------------|
| **Time to setup** | 5 minutes | 30-60 minutes |
| **Azure account** | Not required | Required |
| **Persistence** | ❌ In-memory only | ✅ Cosmos DB |
| **OTP storage** | ❌ In-memory only | ✅ Redis |
| **Scalability** | ❌ Single instance | ✅ Multi-instance |
| **Notifications** | ❌ No SMS/email | ✅ Via Azure Communication Services |
| **Production ready** | ❌ Development only | ✅ Yes |

## Learning Path

Recommended order for new users:

1. **Start** → [Quickstart](quickstart.md) (5 min)
2. **Explore** → Try features, experiment with the UI
3. **Understand** → [Architecture Overview](../architecture/overview.md)
4. **Deploy** → [Full Installation](installation.md) (if you need persistence)
5. **Develop** → [Local Development Setup](../development/local-setup.md) (if contributing)

## System Requirements

### Minimum Requirements
- **OS**: Windows, macOS, or Linux
- **Runtime**: .NET 10.0 SDK
- **Browser**: Modern browser (Chrome, Firefox, Edge, Safari)
- **RAM**: 2 GB available
- **Disk**: 500 MB for source code and dependencies

### For Azure Deployment
- **Azure Subscription**: Active subscription with contributor access
- **Azure CLI**: Version 2.50.0 or later
- **Bicep CLI**: Version 0.20.0 or later (included with Azure CLI)

## Common Scenarios

### "I want to see what this does"
➡️ Start with [Quickstart](quickstart.md) - 5 minutes, no Azure needed

### "I want to run this in production"
➡️ Follow [Full Installation](installation.md) → [Production Checklist](../deployment/production-checklist.md)

### "I want to contribute code"
➡️ Read [Contributing Guide](../../CONTRIBUTING.md) → [Development Setup](../development/local-setup.md)

### "I want to understand how it works"
➡️ Check [Architecture Overview](../architecture/overview.md) → [Feature Guides](../features/)

## Next Steps

Choose your path:

- 🚀 **Quick Try** → [Quickstart Guide](quickstart.md)
- 📚 **Learn First** → [Architecture Overview](../architecture/overview.md)
- 🛠️ **Full Setup** → [Installation Guide](installation.md)
- 💻 **Develop** → [Local Development](../development/local-setup.md)

---

**Need help?** Check the [FAQ](../reference/faq.md) or [open an issue](https://github.com/smereczynski/SignalR-Chat/issues)

[Back to documentation home](../README.md)
