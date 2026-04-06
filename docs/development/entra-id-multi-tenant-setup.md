# Entra ID Multi-Tenant Setup

## Summary

Entra ID is the primary enterprise authentication path. Users are not auto-provisioned. A matching application user must already exist in Cosmos DB before Entra login is accepted.

OTP remains available as a failover login path.

## Required User Record

Before the first Entra login, insert a user that contains at least:

```json
{
  "userName": "michal.s@free-media.eu",
  "upn": "michal.s@free-media.eu",
  "enabled": true,
  "dispatchCenterId": "dc-a"
}
```

Important:

- `upn` is the strict lookup field for Entra authentication
- `dispatchCenterId` is required for chat access after login
- `enabled = false` blocks both Entra and OTP access

## App Registration

Configure:

- redirect URI for local and deployed environments
- application role `Admin.ReadWrite`
- multi-tenant sign-in according to your environment policy

## Runtime Behavior

On token validation the application:

1. extracts `preferred_username`
2. validates tenant rules
3. loads the user by `Upn`
4. updates profile fields from token claims
5. rewrites `ClaimTypes.Name` to `ApplicationUser.UserName`

This final step is important because the app uses `User.Identity.Name` internally for repositories, hubs, and controllers.

## Admin Users

Admin access requires both:

- `Admin.ReadWrite` role in Entra ID
- home-tenant validation in the app

External tenant users do not keep admin access even if they present the role claim.

## OTP Failover

If Entra login is not available, OTP can still be used for existing users. OTP does not create missing users and does not bypass dispatch-center assignment requirements.
