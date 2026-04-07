# Local Setup

## Summary

Local development now assumes the dispatch-center pair model. There are no seeded users, no seeded rooms, and no generic `general` room to join.

A working local environment requires:

- Redis
- Cosmos DB configuration
- a manually inserted user record
- at least two corresponding dispatch centers with officers if you want a visible active room

## Prerequisites

- .NET SDK 10
- Redis running locally or reachable from the app
- Cosmos DB connection string configured
- HTTPS development certificate trusted
- Entra ID app registration configured for local callback URLs if you use Entra login

## Configuration

Typical local startup uses `.env.local` plus `appsettings.Development.json` values.

Required areas:

- `Cosmos`
- `Redis`
- `EntraId`
- `Otp`
- `Translation` if translation is enabled

## Minimum Manual Bootstrap

### 1. Insert the first user

Insert a user document that includes:

- `userName`
- `upn`
- `dispatchCenterId`
- `enabled = true`

Example:

```json
{
  "userName": "michal.s@free-media.eu",
  "fullName": "Michal Smereczynski",
  "email": "michal.s@free-media.eu",
  "upn": "michal.s@free-media.eu",
  "enabled": true,
  "preferredLanguage": "pl",
  "dispatchCenterId": "dc-a"
}
```

### 2. Create dispatch centers

Create at least two dispatch centers and link them through `correspondingDispatchCenterIds`.

### 3. Assign officers

Each side must have at least one value in `officerUserNames` for the pair room to be active.

### 4. Start the app

```bash
dotnet run --project ./src/Chat.Web --urls=https://localhost:5099
```

Startup reconciles derived pair rooms from the current topology.

## Expected Outcomes

### User sees no rooms

Check these first:

1. user has `dispatchCenterId`
2. assigned dispatch center exists
3. dispatch center has at least one corresponding dispatch center
4. a corresponding dispatch center points back or is synchronized by admin save
5. both sides have at least one officer

### Entra login succeeds but chat is empty

This usually means the user exists and authenticated correctly, but the topology is incomplete or the user record has no `dispatchCenterId`.

### OTP login

OTP is still supported, but the user must already exist in the database and still needs a valid dispatch-center assignment to access chat.
