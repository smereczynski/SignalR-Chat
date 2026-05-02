# Dispatch-Center Escalation Implementation

## Summary

This branch implements a strict dispatch-center communication model:

- A user belongs to exactly one dispatch center via `ApplicationUser.DispatchCenterId`.
- Dispatch centers define their allowed communication graph through `CorrespondingDispatchCenterIds`.
- Chat rooms are derived from active dispatch-center pairs and are never seeded or managed manually.
- Each dispatch center has one or more escalation officers in `OfficerUserNames`.
- Escalations target all officers assigned to the counterpart dispatch center.
- OTP remains available as a failover login path; Entra ID is the primary enterprise login path.

There is no backward compatibility with the legacy room-based chat model. `FixedRooms`, `DefaultRoom`, legacy room seeding, and standard chat rooms are not part of the product anymore.

## Current Domain Model

### Application user

`ApplicationUser` is the runtime identity and authorization source.

- `UserName`: application identity used inside the app
- `Upn`: Entra ID identity used for strict pre-provisioned login matching
- `DispatchCenterId`: authoritative dispatch-center assignment
- `Enabled`: login and chat access switch
- Profile fields such as `FullName`, `Email`, `MobileNumber`, `PreferredLanguage`

### Dispatch center

`DispatchCenter` models topology and escalation routing.

- `Id`
- `Name`
- `Country`
- `IfMain`
- `CorrespondingDispatchCenterIds`
- `Users`
- `OfficerUserNames`

### Room

`Room` is derived from dispatch-center topology.

- `RoomType = DispatchCenterPair`
- `PairKey`
- `DispatchCenterAId`
- `DispatchCenterBId`
- `IsActive`
- `Users`
- `Languages`

Rooms are synchronized from dispatch-center state. A pair room is active only when both sides have at least one escalation officer.

### Escalation

`Escalation` represents either a scheduled automatic escalation or an immediate manual escalation.

- `SourceDispatchCenterId`
- `TargetDispatchCenterId`
- `TargetOfficerUserNames`
- `TriggerType`
- `Status`
- `MessageIds`
- `MessageSnapshots`

## Runtime Behavior

### Authorization

Room access is allowed only when all of the following are true:

1. The user exists and is enabled.
2. The user has a non-empty `DispatchCenterId`.
3. The room is a dispatch-center pair room.
4. The room is active.
5. The room includes the user’s dispatch center.

### Room derivation

Pair rooms are rebuilt from dispatch-center topology:

1. Load all dispatch centers.
2. Expand each `CorrespondingDispatchCenterIds` relation into a normalized pair key.
3. Build or update the pair room.
4. Copy assigned users and preferred languages from both dispatch centers into the room document.
5. Archive stale pair rooms that are no longer present in topology.

Startup runs a reconciliation pass so an existing database can self-heal even if no admin mutation has occurred recently.

### Escalation flow

Automatic escalation:

1. User sends a message into an active pair room.
2. The message stores `FromDispatchCenterId`.
3. An automatic escalation is scheduled for the counterpart dispatch center.
4. If the counterpart dispatch center reads the message before the due time, the escalation resolves.
5. If not, the escalation becomes `Escalated` and notifications are sent to `TargetOfficerUserNames`.

Manual escalation:

1. User selects messages authored by their own dispatch center.
2. Messages must belong to the active pair room and must not already be acknowledged by the counterpart dispatch center.
3. A manual escalation is created immediately in `Escalated` status.
4. Notifications are sent to all officers assigned to the counterpart dispatch center.

## Admin Workflows

The admin surface is intentionally narrow:

- Create users
- Assign a user to one dispatch center
- Create and edit dispatch centers
- Assign escalation officers
- Define corresponding dispatch-center relations

There is no admin room management because rooms are derived from topology.

## Bootstrap Rules

For a fresh environment:

1. Insert the first user manually with `userName`, `upn`, `dispatchCenterId`, and `enabled = true`.
2. Create dispatch centers.
3. Add `OfficerUserNames` on both sides of any desired communication pair.
4. Add corresponding dispatch-center relations.
5. Start the application or trigger topology sync.

If any of those pieces are missing, the user may authenticate successfully but still see no accessible rooms.

## Acceptance Checklist

- No runtime path relies on legacy room assignment fields.
- Users only see pair rooms involving their assigned dispatch center.
- Pair rooms are derived automatically on startup and on topology changes.
- Escalations target officer lists on the counterpart dispatch center.
- No seeded users or seeded standard rooms are required.
