# Documentation Status

**Updated**: 2026-04-06  
**Branch**: `feat/dispatch-center-escalations-v1`

## Summary

Documentation has been realigned to the current branch direction:

- dispatch-center pair chat
- multi-officer escalations
- single dispatch-center assignment per user
- Entra ID primary login with OTP failover
- no seeded users or seeded standard rooms

## Important Current-State Rules

- `ApplicationUser.DispatchCenterId` is the only authoritative chat assignment field.
- `DispatchCenter.OfficerUserNames` and `Escalation.TargetOfficerUserNames` are list-based.
- Rooms are derived from dispatch-center topology and are not managed directly.
- Local setup and bootstrap now require manual creation of the first user and dispatch-center topology.

## Reviewed and Rewritten Areas

- architecture overview
- data model
- system design
- dispatch-center escalation feature documentation
- local setup
- bootstrap
- admin panel integration
- Entra ID setup

## Remaining Guidance

When updating docs in this branch, treat any mention of the following as stale unless it is explicitly historical:

- `FixedRooms`
- `DefaultRoom`
- `DispatchCenterIds`
- seeded `general`, `ops`, or `random` rooms
- singular officer fields
- admin room management
