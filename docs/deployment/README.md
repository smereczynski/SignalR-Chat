# Deployment

This section covers deployment and environment bootstrap for the dispatch-center pair chat model.

## Core Deployment Docs

- [Bootstrap](bootstrap.md)
- [Azure Deployment](azure/README.md)
- [GitHub Actions](github-actions.md)
- [GitHub Secrets](github-secrets.md)
- [GitHub Variables](github-variables.md)
- [Production Checklist](production-checklist.md)
- [Post-Deployment Manual Steps](post-deployment-manual-steps.md)

## What Bootstrap Means Now

Deployment bootstrap no longer means creating seeded rooms or sample users.

It now means:

1. provisioning infrastructure
2. configuring app settings and secrets
3. inserting the first user manually with `upn` and `dispatchCenterId`
4. creating dispatch centers
5. assigning officers and corresponding dispatch-center relations
6. letting topology sync derive pair rooms automatically

## Deployment Checklist

- [x] infrastructure can host dispatch-center pair chat
- [x] bootstrap docs describe manual first-user creation
- [x] topology-derived rooms replace manual room management
- [x] OTP remains documented as failover login
