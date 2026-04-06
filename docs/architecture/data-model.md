# Data Model

## Summary

The application stores four primary business aggregates in Cosmos DB:

- users
- dispatch centers
- rooms
- escalations

Messages remain stored separately and reference a derived dispatch-center pair room.

The current system is centered on dispatch-center assignment and pair-room derivation. Legacy room membership fields are not part of the model.

## Users

`ApplicationUser` is the authoritative identity record used for login, authorization, presence, and message attribution.

Example:

```json
{
  "id": "82d2f999-b21e-47b0-8f5c-43da1728add4",
  "userName": "michal.s@free-media.eu",
  "fullName": "Michal Smereczynski",
  "preferredLanguage": "pl",
  "email": "michal.s@free-media.eu",
  "mobile": "+48604970937",
  "enabled": true,
  "upn": "michal.s@free-media.eu",
  "tenantId": "6d338245-9261-4f6d-a5a1-cd18b014a259",
  "displayName": "Michal Smereczynski",
  "country": "Poland",
  "region": "Centrala",
  "dispatchCenterId": "f8e63dd7-6ae8-4fd2-af62-cc35d67dd49f"
}
```

Important fields:

- `userName`: app identity used throughout the codebase
- `upn`: Entra ID identity used for strict login lookup
- `dispatchCenterId`: authoritative chat assignment
- `enabled`: access switch
- `preferredLanguage`: source language for translation and room language aggregation

## Dispatch centers

`DispatchCenter` defines the communication topology and escalation routing.

Example:

```json
{
  "id": "f8e63dd7-6ae8-4fd2-af62-cc35d67dd49f",
  "name": "Dyspozytura 1",
  "country": "PL",
  "ifMain": false,
  "correspondingDispatchCenterIds": [
    "a1bb1c0d-c515-4eb2-81de-78cf41ffdc2e"
  ],
  "users": [
    "michal.s@free-media.eu",
    "jan.kowalski@free-media.eu"
  ],
  "officerUserNames": [
    "michal.s@free-media.eu",
    "jan.kowalski@free-media.eu"
  ]
}
```

Important fields:

- `correspondingDispatchCenterIds`: allowed pair relations
- `users`: denormalized membership
- `officerUserNames`: escalation targets for messages escalated toward this dispatch center

## Rooms

`Room` is a derived projection of a dispatch-center pair. It is not a manually managed entity.

Example:

```json
{
  "id": "157284294",
  "name": "pair:a1bb1c0d-c515-4eb2-81de-78cf41ffdc2e::f8e63dd7-6ae8-4fd2-af62-cc35d67dd49f",
  "displayName": "Dyspozytura 1 <-> Dyspozytura 2",
  "roomType": "DispatchCenterPair",
  "pairKey": "a1bb1c0d-c515-4eb2-81de-78cf41ffdc2e::f8e63dd7-6ae8-4fd2-af62-cc35d67dd49f",
  "dispatchCenterAId": "a1bb1c0d-c515-4eb2-81de-78cf41ffdc2e",
  "dispatchCenterBId": "f8e63dd7-6ae8-4fd2-af62-cc35d67dd49f",
  "isActive": true,
  "users": [
    "michal.s@free-media.eu",
    "jan.kowalski@free-media.eu"
  ],
  "languages": [
    "pl",
    "de"
  ]
}
```

Important notes:

- All rooms are dispatch-center pair rooms.
- Rooms are created and refreshed from topology sync.
- `isActive` depends on both sides having at least one officer.

## Messages

Messages are scoped to a derived room and record the sending dispatch center.

Example:

```json
{
  "id": 123,
  "roomName": "pair:a::b",
  "content": "Need response from counterpart dispatch center",
  "fromUser": "michal.s@free-media.eu",
  "fromDispatchCenterId": "a",
  "timestamp": "2026-04-06T13:07:56Z",
  "readByDispatchCenterIds": [
    "b"
  ],
  "escalationStatus": "Resolved"
}
```

## Escalations

Escalations track unread or manually escalated messages.

Example:

```json
{
  "id": "0d2115c0-6f13-4f2d-a798-58d965ad3d1a",
  "roomName": "pair:a::b",
  "pairKey": "a::b",
  "sourceDispatchCenterId": "a",
  "targetDispatchCenterId": "b",
  "targetOfficerUserNames": [
    "officer.b1@contoso.com",
    "officer.b2@contoso.com"
  ],
  "triggerType": "Automatic",
  "status": "Scheduled",
  "messageIds": [
    123
  ]
}
```

## Invariants

- A user may belong to one dispatch center at a time.
- A room is accessible only if it includes the user’s dispatch center and `isActive = true`.
- Pair rooms are derived data and may be rebuilt from topology.
- Escalations target officer lists, not a single officer field.
