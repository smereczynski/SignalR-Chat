# Admin.Web

A minimal, secure admin panel (Razor Pages) deployed separately on Azure App Service.

- Authentication: Entra ID (Microsoft Identity Platform, OpenID Connect). No application-local auth.
- Data: Cosmos DB (users, rooms). No chat/message features.
- Features:
  - Create users (email, GSM), set Enabled flag
  - Create rooms
  - Assign users to rooms
  - Authenticated operators see all users/rooms

Configuration
- AzureAd: configure Instance, TenantId, ClientId
- Cosmos: ConnectionString, Database, UsersContainer, RoomsContainer
- Authorization: ChatAdmin group object ID (see below)

Security
- All pages require authentication and membership in the ChatAdmin Entra ID group
- Configuration key: `Authorization:ChatAdminGroupObjectId`
- Ensure the app registration emits group claims in ID tokens (Token configuration → Group claims). If group overage occurs, consider limiting to “Groups assigned to the application” or using App Roles instead.
- No client-side JS frameworks; simple server-rendered forms

Sign-in/out URLs (local/dev)
- Redirect URI (sign-in): https://localhost:5199/signin-oidc
- Front-channel logout URL: https://localhost:5199/signout-oidc
- Sign-out endpoint (UI): https://localhost:5199/MicrosoftIdentity/Account/SignOut

Health
- Anonymous health probe at `/healthz` returns `ok`

Run locally
- Use the VS Code task “Run Admin (Dev)” (trusts HTTPS dev certs and serves on https://localhost:5199)
- Or run manually:
  - Trust HTTPS dev certs once: `dotnet dev-certs https --trust`
  - Run: `ASPNETCORE_URLS=https://localhost:5199 dotnet run --project ./src/Admin.Web`

Tests
- Minimal integration tests exist under `tests/Admin.Web.Tests`.
- Run: `dotnet test ./src/Admin.sln`

Authorization config example (appsettings.json)
```
"Authorization": {
  "ChatAdminGroupObjectId": "62dc8cc5-fd83-45ba-b40f-64449d26e0de"
}
```

Environment variables (zsh)
```
export Authorization__ChatAdminGroupObjectId=62dc8cc5-fd83-45ba-b40f-64449d26e0de
```

Deploy
- Build and deploy as a separate Azure App Service