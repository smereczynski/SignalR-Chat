# Admin.Web

A minimal, secure admin panel (Razor Pages) deployed separately on Azure App Service.

- Authentication: Entra ID (Microsoft Identity Platform, OpenID Connect). No application-local auth.
- Data: Cosmos DB (users, rooms). No chat/message features.
- Features:
  - Create users (email, GSM), set Admin and Enabled flags
  - Create rooms
  - Assign users to rooms
  - Admins see all users/rooms

Configuration
- AzureAd: configure Instance, TenantId, ClientId
- Cosmos: ConnectionString, Database, UsersContainer, RoomsContainer

Security
- All pages require authentication
- No client-side JS frameworks; simple server-rendered forms

Deploy
- Build and deploy as a separate Azure App Service