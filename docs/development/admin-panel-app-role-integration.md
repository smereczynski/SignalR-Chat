# Admin Panel and App Role Integration

## Summary

Admin access is controlled by the Entra application role and home-tenant validation. The admin UI manages users and dispatch-center topology, not manual chat rooms.

## Admin Policy

Admin pages require:

- authenticated user
- Entra role `Admin.ReadWrite`
- home-tenant validation

## Current Admin Pages

| Area | Purpose |
| --- | --- |
| `/Admin` | Admin landing page |
| `/Admin/Users` | List users, enable or disable users |
| `/Admin/Users/Create` | Create a user |
| `/Admin/Users/AssignDispatchCenter/{userName}` | Assign exactly one dispatch center to a user |
| `/Admin/DispatchCenters` | Manage dispatch centers |
| `/Admin/DispatchCenters/Create` | Create dispatch center |
| `/Admin/DispatchCenters/Edit/{id}` | Edit corresponding pairs and officers |
| `/Admin/DispatchCenters/AssignUsers/{id}` | Bulk assign users to a dispatch center |

## What Admins Control

Admins are responsible for:

- creating user records
- pre-provisioning `upn` for Entra login
- assigning users to a single dispatch center
- assigning escalation officers
- configuring dispatch-center pair relations

Admins do not create chat rooms directly. Rooms are derived from topology.

## Operational Notes

- A user may log in successfully but still see no rooms if the admin did not assign `dispatchCenterId`.
- A pair room may exist but remain inactive until both sides have officers.
- Removing a dispatch-center assignment removes room access immediately.
