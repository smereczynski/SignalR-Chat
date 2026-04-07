# Azure Deployment

This directory contains Azure infrastructure documentation for the current dispatch-center pair chat application.

## Docs

- [Bicep Templates](bicep-templates.md)
- [Bootstrap](../bootstrap.md)
- [GitHub Actions](../github-actions.md)
- [Production Checklist](../production-checklist.md)

## Infrastructure Context

Azure hosts the application services that support:

- Entra ID and OTP-backed authentication
- Cosmos DB persistence for users, dispatch centers, rooms, messages, and escalations
- Redis-backed OTP, presence, and queue state
- SignalR transport
- optional translation and notification integrations

Rooms are still derived from dispatch-center topology at runtime. Infrastructure does not pre-seed chat rooms.

## Deployment Checklist

- [x] Cosmos and Redis required for persistent runtime behavior
- [x] app configuration must support Entra ID and OTP
- [x] bootstrap must insert the first user and dispatch-center topology
