# System Design

## Summary

The application is a dispatch-center pair chat system with escalation routing.

- Authentication: Entra ID primary, OTP failover
- Authorization: dispatch-center pair room access
- Persistence: Cosmos DB
- Presence and queues: Redis
- Realtime transport: SignalR
- Translation: asynchronous translation pipeline
- Escalation: automatic and manual dispatch-center escalation workflow

## High-Level Flow

1. User authenticates with Entra ID or OTP.
2. The application resolves the `ApplicationUser` record.
3. The user’s `DispatchCenterId` determines which pair rooms are accessible.
4. The client auto-joins the first accessible derived pair room.
5. Messages store `FromDispatchCenterId`.
6. Read acknowledgements are tracked per dispatch center.
7. Escalations route to officer lists on the counterpart dispatch center.

## Authentication and Identity

### Entra ID

Entra ID login is strict and pre-provisioned.

- The incoming UPN must match `ApplicationUser.Upn`.
- On successful login, the app rewrites `ClaimTypes.Name` to `ApplicationUser.UserName`.
- Home-tenant admin role handling is enforced for admin pages.

### OTP failover

OTP remains supported for failover scenarios.

- User must already exist in the application database.
- OTP does not bypass `Enabled` or dispatch-center authorization rules.

## Authorization Model

Authorization is dispatch-center based, not room-list based.

Access to a room requires:

1. enabled user
2. non-empty `DispatchCenterId`
3. `RoomType = DispatchCenterPair`
4. active room
5. room includes the user’s dispatch center

This same policy is used for room listing, room join, message read, retry translation, and escalation flows.

## Topology and Derived Rooms

`DispatchCenterTopologyService` is the source of truth for pair-room derivation.

Responsibilities:

- assign user to one dispatch center
- remove user from a dispatch center
- persist symmetric pair relations
- derive pair rooms
- archive pair rooms that are no longer valid
- refresh room users and aggregated languages

Startup also performs topology reconciliation so an existing database can recover missing room projections without manual admin edits.

## Messaging

Messages are sent only inside active pair rooms.

- Each message stores `FromDispatchCenterId`.
- Read state is tracked both per user and per dispatch center.
- Translation targets come from the room’s aggregated language list.

## Escalations

Two escalation modes exist:

### Automatic

- scheduled when a message is sent
- resolved if the counterpart dispatch center acknowledges the message in time
- escalated if unread at due time

### Manual

- created by selecting messages from the sender’s own dispatch center
- only valid inside the active pair room
- sent immediately to the counterpart dispatch center’s officers

## Admin Surface

Admin pages are intentionally focused on topology management:

- users
- assign dispatch center
- dispatch centers
- assign users
- officer selection
- corresponding dispatch-center configuration

There is no room-management admin surface because rooms are derived projections.

## Operational Assumptions

- No seeded rooms or seeded users are required.
- The first user may be inserted manually into Cosmos DB.
- That user must include `upn` and `dispatchCenterId`.
- Missing or incomplete topology results in an empty room list, not implicit fallback chat behavior.
