# SignalR Chat Documentation

Welcome to the SignalR Chat documentation! This guide will help you understand, deploy, and contribute to the project.

## 📚 Documentation Structure

### 🚀 Getting Started
New to SignalR Chat? Start here!

- **[Quickstart](getting-started/quickstart.md)** - Run locally in 5 minutes (no Azure required)
- **[Installation](getting-started/installation.md)** - Full installation guide with Azure resources
- **[Configuration](getting-started/configuration.md)** - Environment variables and options reference

### 🏗️ Architecture
Understand how the system works.

- **[Overview](architecture/overview.md)** - System design and components
- **[Data Model](architecture/data-model.md)** - Cosmos DB schema and Redis keys
- **[Security](architecture/security.md)** - Security architecture and threat model
- **[Diagrams](architecture/diagrams/)** - Visual architecture diagrams
- **[Decisions (ADRs)](architecture/decisions/)** - Architecture Decision Records

### ✨ Features
Learn about specific features and how they work.

- **[Authentication](features/authentication.md)** - Dual authentication (Entra ID + OTP)
- **[Entra ID Setup](development/entra-id-multi-tenant-setup.md)** - Multi-tenant configuration
- **[Sessions](features/sessions.md)** - Session management
- **[Presence Tracking](features/presence.md)** - Online/offline status
- **[Real-time Messaging](features/real-time-messaging.md)** - SignalR implementation
- **[Read Receipts](features/read-receipts.md)** - Message read status
- **[Notifications](features/notifications.md)** - Email/SMS notifications
- **[Localization](features/localization.md)** - Multi-language support
- **[Rate Limiting](features/rate-limiting.md)** - Abuse prevention
- **[Pagination](features/pagination.md)** - Message pagination

### 🚀 Deployment
Deploy to Azure and configure environments.

- **[Azure Deployment](deployment/azure/)** - Deploy to Azure with Bicep
- **[Environments](deployment/environments.md)** - Dev, Staging, Production configs
- **[GitHub Actions](deployment/github-actions.md)** - CI/CD pipelines
- **[Production Checklist](deployment/production-checklist.md)** - Pre-launch checklist
- **[Troubleshooting](deployment/troubleshooting.md)** - Common deployment issues

### 💻 Development
Contributing and local development.

- **[Local Setup](development/local-setup.md)** - Set up your development environment
- **[Project Structure](development/project-structure.md)** - Code organization
- **[Testing](development/testing.md)** - Running and writing tests
- **[Debugging](development/debugging.md)** - Debugging tips and tools
- **[VS Code Setup](development/vscode-setup.md)** - VS Code configuration
- **[Contributing](../CONTRIBUTING.md)** - How to contribute

### 🔧 Operations
Running and monitoring in production.

- **[Monitoring](operations/monitoring.md)** - Observability overview
- **[OpenTelemetry](operations/opentelemetry.md)** - OpenTelemetry configuration
- **[Application Insights](operations/application-insights.md)** - Azure monitoring
- **[Logging](operations/logging.md)** - Logging best practices
- **[Diagnostics](operations/diagnostics.md)** - Troubleshooting production issues
- **[Health Checks](operations/health-checks.md)** - Health endpoint details
- **[Performance](operations/performance.md)** - Performance tuning

### 📖 Reference
Technical reference documentation.

- **[REST API](reference/api/rest-endpoints.md)** - HTTP endpoints
- **[SignalR Hub](reference/api/signalr-hub.md)** - WebSocket methods
- **[Configuration Reference](reference/configuration-reference.md)** - All config options
- **[Telemetry Reference](reference/telemetry-reference.md)** - Metrics and traces
- **[FAQ](reference/faq.md)** - Frequently asked questions
- **[Glossary](reference/glossary.md)** - Terms and definitions

---

## 🔍 Quick Links

- **[Back to main README](../README.md)**
- **[Architecture Overview](architecture/overview.md)**
- **[5-Minute Quickstart](getting-started/quickstart.md)**
- **[Production Checklist](deployment/production-checklist.md)**
- **[Contributing Guide](../CONTRIBUTING.md)**
- **[Changelog](../CHANGELOG.md)**

---

## 📝 Documentation Standards

This documentation follows the [Diátaxis framework](https://diataxis.fr/):

- **Tutorials** (learning-oriented) → Getting Started
- **How-to guides** (problem-oriented) → Features, Deployment, Development, Operations
- **Reference** (information-oriented) → Reference
- **Explanation** (understanding-oriented) → Architecture

---

**Last updated**: November 2025 | **Version**: 0.9.5
