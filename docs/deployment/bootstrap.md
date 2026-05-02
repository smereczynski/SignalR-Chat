# Bootstrap

## Summary

The application no longer bootstraps seeded chat rooms or seeded sample users. Bootstrap is now about preparing identity and dispatch-center topology so the app can derive pair rooms automatically.

## Fresh Environment Checklist

1. Provision Cosmos DB, Redis, SignalR, and optional translation dependencies.
2. Configure application settings and secrets.
3. Insert the first application user manually.
4. Create dispatch centers.
5. Assign users to dispatch centers.
6. Assign one or more officers to each dispatch center that should participate in chat.
7. Configure corresponding dispatch-center pairs.
8. Start the app and let topology sync create derived pair rooms.

## First User

The first user must be created directly in the `users` container before Entra login can succeed.

Required fields:

- `userName`
- `upn`
- `dispatchCenterId`
- `enabled`

Recommended fields:

- `fullName`
- `email`
- `mobile`
- `preferredLanguage`

## Dispatch Center Bootstrap

Each dispatch center document should include:

- `id`
- `name`
- `country`
- `ifMain`
- `correspondingDispatchCenterIds`
- `users`
- `officerUserNames`

## Pair Room Activation Rules

A pair room exists as a derived projection of dispatch-center topology.

A pair room is usable only when:

- both dispatch centers are linked as a pair
- at least one user is assigned to the relevant dispatch center
- both dispatch centers have at least one escalation officer

## Validation

After bootstrap:

1. Log in as the provisioned user.
2. Call `GET /api/auth/me` and verify `dispatchCenterId`.
3. Call `GET /api/Rooms`.
4. Confirm the user receives only dispatch-center pair rooms involving their own dispatch center.

If `GET /api/Rooms` returns an empty array, bootstrap is incomplete; the application does not fall back to generic rooms.
