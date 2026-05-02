# Getting Started

Use this section to understand how to run the current dispatch-center pair chat implementation.

## Recommended Reading Order

1. [Architecture Overview](../architecture/overview.md)
2. [Local Setup](../development/local-setup.md)
3. [Bootstrap](../deployment/bootstrap.md)
4. [Dispatch-Center Escalation Implementation](../features/dispatch-center-escalation-implementation-plan.md)

## Entry Points

- [Configuration](configuration.md)
- [Installation](installation.md)
- [Quickstart](quickstart.md)

## Important Context

The current branch does not use seeded users or seeded rooms.

To validate the real dispatch-center workflow locally or in Azure, you need:

- a user with `upn` and `dispatchCenterId`
- dispatch centers with pair relations
- officers assigned on both sides of the pair

`quickstart.md` is still useful for lightweight application bring-up, but the current product behavior is best validated through [Local Setup](../development/local-setup.md).

## Checklist

- [x] understand that rooms are derived from topology
- [x] provision a real user record before Entra login
- [x] configure dispatch centers and officers before expecting visible chat rooms
