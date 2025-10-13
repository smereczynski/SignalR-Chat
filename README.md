# Admin.Web (Entra ID) — Management Panel

This branch contains a separate ASP.NET Core Razor Pages application for administration tasks, authenticated with Microsoft Entra ID only. It does not include in-app OTP auth or SignalR chat functionality.

## What’s here
- Project: `src/Admin.Web` (TargetFramework: net9.0)
- Auth: Microsoft.Identity.Web (OpenID Connect) — sign-in/out via Entra ID
- Authorization: default policy requires membership in a specific Entra ID security group (ChatAdmin)
- Data: Cosmos DB (users, rooms) via lightweight repository pattern
- UI: Razor Pages, minimal CSS reusing styles consistent with Chat.Web

## Features
- Users
  - List all users (email, mobile, enabled, rooms)
  - Create user
  - Toggle Enabled flag
  - Assign rooms
- Rooms
  - List rooms
  - Create room

## Configuration
Set the following in `appsettings.json` or environment variables:

Azure AD (Entra ID):
```
"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "Domain": "<your-tenant-domain>",
  "TenantId": "<tenant-guid>",
  "ClientId": "<app-registration-client-id>",
  "CallbackPath": "/signin-oidc"
}
```

Cosmos DB:
```
"Cosmos": {
  "ConnectionString": "AccountEndpoint=https://<acct>.documents.azure.com:443/;AccountKey=<key>;",
  "Database": "chat",
  "UsersContainer": "users",
  "RoomsContainer": "rooms"
}
```

Environment variables (zsh examples):
```
export AzureAd__TenantId=00000000-0000-0000-0000-000000000000
export AzureAd__ClientId=11111111-1111-1111-1111-111111111111
export AzureAd__Domain=contoso.onmicrosoft.com
export Cosmos__ConnectionString="AccountEndpoint=...;AccountKey=...;"
export Authorization__ChatAdminGroupObjectId=62dc8cc5-fd83-45ba-b40f-64449d26e0de
```

## Run locally
Preferred: use VS Code task “Run Admin (Dev)” which trusts the dev cert and serves on HTTPS.

Manual:
```
dotnet dev-certs https --trust
ASPNETCORE_URLS=https://localhost:5199 dotnet run --project ./src/Admin.Web
```
Open https://localhost:5199 — you’ll be redirected to Entra ID to sign in.

## Notes
- All pages require authentication by default; sign-out is available at `/MicrosoftIdentity/Account/SignOut`.
- Ensure Entra ID app registration includes:
  - Redirect URI: `https://localhost:5199/signin-oidc`
  - Front-channel logout URL: `https://localhost:5199/signout-oidc`
- To authorize via groups, ensure Token configuration adds group claims to ID tokens. If tokens become too large (group overage), prefer limiting to “Groups assigned to the application” or switch to App Roles.
- Health endpoint: `/healthz` returns `ok`.
- Repositories are minimal and assume existing Cosmos database and containers.
- Styles are lightweight and primarily server-rendered Razor Pages; no SignalR/JS chat code is included in Admin.Web.

## License
See `LICENSE`.
