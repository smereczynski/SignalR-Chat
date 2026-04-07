# Documentation

This documentation set reflects the current dispatch-center pair chat implementation on `feat/dispatch-center-escalations-v1`.

## Start Here

- [Architecture Overview](architecture/overview.md)
- [Dispatch-Center Escalation Implementation](features/dispatch-center-escalation-implementation-plan.md)
- [Local Setup](development/local-setup.md)
- [Bootstrap](deployment/bootstrap.md)
- [Documentation Status](DOCUMENTATION-STATUS.md)

## Sections

### Getting Started

- [Getting Started](getting-started/README.md)
- [Configuration](getting-started/configuration.md)
- [Installation](getting-started/installation.md)
- [Quickstart](getting-started/quickstart.md)

### Architecture

- [Overview](architecture/overview.md)
- [Data Model](architecture/data-model.md)
- [System Design](architecture/system-design.md)
- [Translation Architecture](architecture/translation-architecture.md)
- [Architecture Decisions](architecture/decisions/)

### Features

- [Features Index](features/README.md)
- [Authentication](features/authentication.md)
- [Dispatch-Center Escalation Implementation](features/dispatch-center-escalation-implementation-plan.md)
- [Presence](features/presence.md)
- [Sessions](features/sessions.md)
- [Async Translation Plan](features/async-translation-implementation-plan.md)

### Development

- [Local Setup](development/local-setup.md)
- [Testing](development/testing.md)
- [Admin Panel and App Role Integration](development/admin-panel-app-role-integration.md)
- [Entra ID Multi-Tenant Setup](development/entra-id-multi-tenant-setup.md)
- [Integration Test Improvements](development/integration-tests-improvements.md)

### Deployment

- [Deployment Index](deployment/README.md)
- [Bootstrap](deployment/bootstrap.md)
- [Azure Deployment](deployment/azure/README.md)
- [GitHub Actions](deployment/github-actions.md)
- [Production Checklist](deployment/production-checklist.md)

### Operations

- [Monitoring](operations/monitoring.md)
- [Disaster Recovery](operations/disaster-recovery.md)

### Reference

- [FAQ](reference/faq.md)

## Current-State Checklist

- [x] Users are assigned to one dispatch center
- [x] Rooms are derived from dispatch-center pairs
- [x] Escalations target officer lists on the counterpart dispatch center
- [x] Entra ID is primary login
- [x] OTP remains available as failover
- [x] No seeded standard rooms or seeded users are required
