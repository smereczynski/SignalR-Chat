# SignalR Chat

SignalR Chat is a dispatch-center pair chat application built with ASP.NET Core, SignalR, Cosmos DB, Redis, and Azure-hosted services. The current branch implements strict dispatch-center topology, multi-officer escalations, Entra ID admin login, and OTP failover authentication.

## Current Product Model

- Users belong to exactly one dispatch center through `ApplicationUser.DispatchCenterId`.
- Dispatch centers communicate only with configured corresponding dispatch centers.
- Rooms are derived from dispatch-center pairs and synchronized automatically.
- Each dispatch center can have multiple escalation officers.
- Escalations target all officers assigned to the counterpart dispatch center.
- OTP remains available as a failover login path.

There are no standard chat rooms, no seeded rooms, and no legacy room-assignment compatibility in the current implementation.

## Quick Links

- [Documentation Home](docs/README.md)
- [Architecture Overview](docs/architecture/overview.md)
- [Dispatch-Center Escalation Implementation](docs/features/dispatch-center-escalation-implementation-plan.md)
- [Local Setup](docs/development/local-setup.md)
- [Bootstrap Guide](docs/deployment/bootstrap.md)
- [Changelog](CHANGELOG.md)

## Local Development

For the current branch behavior, use the dispatch-center setup described in [Local Setup](docs/development/local-setup.md).

At minimum you need:

1. Redis and Cosmos DB configuration
2. a manually inserted user with `upn` and `dispatchCenterId`
3. dispatch centers with corresponding relations
4. officers assigned on both sides of a desired pair

Then run:

```bash
dotnet build src/Chat.sln
dotnet run --project ./src/Chat.Web --urls=https://localhost:5099
```

## Verification

Current verification status on this branch:

- `dotnet build src/Chat.sln` passes
- `dotnet test src/Chat.sln --no-build --nologo` passes
- Development startup succeeds without the previous `CosmosClients not yet initialized` failure
- topology reconciliation runs on startup and rebuilds derived pair rooms

## Sprint Implementation Checklist

- [x] Replaced fragile Cosmos startup initialization with deterministic startup behavior
- [x] Added startup dispatch-center topology reconciliation
- [x] Preserved pair-room authorization as the single access model
- [x] Aligned in-memory repository behavior with Cosmos UPN lookup semantics
- [x] Renamed admin user assignment flow to dispatch-center-specific naming
- [x] Added clearer chat empty states for missing dispatch-center topology
- [x] Rewrote stale dispatch-center, architecture, setup, bootstrap, and admin docs
- [x] Removed stale documentation references to seeded standard rooms and legacy room assignment

## Notes

- This repository may still contain unrelated in-progress branch changes outside the work summarized above.
- Some historical docs and test fixtures still mention generic room names for isolated examples; current product behavior is dispatch-center pair chat only.
