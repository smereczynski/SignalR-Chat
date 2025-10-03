# SignalR-Chat
A real-time chat application using .NET 9, Azure SignalR, Cosmos DB, Redis, and KnockoutJS.

Live Demo: https://demo2-kouki.azurewebsites.net/

## Current features
- Group chat and private chat (`/private(Name) Hello`)
- Chat rooms (create, update, delete)
- Text-only messages (uploads and emoji UI removed)
- TOTP-only authentication with cookie sessions
  - OTP code delivery via Azure Communication Services (Email/SMS) when configured
  - Console fallback for OTP during local development

## Architecture
- Real-time: Azure SignalR (always used, local and Azure)
- Storage: Azure Cosmos DB (SQL API) with containers and partition keys
  - users: partition /userName
  - rooms: partition /name
  - messages: partition /roomName (with recommended TTL)
- OTP state: Azure Cache for Redis
- Auth: cookie-based session; UI uses OTP modal to start/verify

## Local development (macOS)
Prereqs: .NET 9 SDK, Azure resources (SignalR, Cosmos DB, Redis). No local DB.

Recommended: use the VS Code task that loads `.env.local` and runs the app in Development.

1) Create `.env.local` by copying the example and filling your values:
```
cp .env.local.example .env.local
```

2) Edit `.env.local` with your connection strings:
```
Cosmos__ConnectionString=AccountEndpoint=...;AccountKey=...
Cosmos__Database=chat
Cosmos__UsersContainer=users
Cosmos__RoomsContainer=rooms
Cosmos__MessagesContainer=messages
Redis__ConnectionString=yourredis:6380,password=...,ssl=True,abortConnect=False
# Optional if the SDK needs it
# Azure__SignalR__ConnectionString=Endpoint=...;AccessKey=...;Version=1.0;
# Optional: Azure Communication Services for OTP delivery
Acs__ConnectionString=Endpoint=...;AccessKey=...
Acs__EmailFrom=from@your-verified-domain.tld
Acs__SmsFrom=+1234567890
```

3) Run from VS Code: Run Task → “Run Chat (Azure local env)”

The app requires all three Azure services (SignalR, Cosmos, Redis) even locally.

## Azure App Service (Windows) - dev/test/prod
Provision:
- Azure SignalR Service
- Azure Cosmos DB (SQL API) with containers: users(/userName), rooms(/name), messages(/roomName)
- Azure Cache for Redis
- App Service (Windows, .NET 9)

Settings:
- Azure SignalR: `Azure:SignalR:ConnectionString` (or equivalent)
- Cosmos: `Cosmos:ConnectionString`, `Cosmos:Database`, `Cosmos:UsersContainer`, `Cosmos:RoomsContainer`, `Cosmos:MessagesContainer`
- Redis: `Redis:ConnectionString`
- Optional ACS: `Acs:ConnectionString`, `Acs:EmailFrom`, `Acs:SmsFrom`
- `ASPNETCORE_ENVIRONMENT` = Development/Staging/Production

Deploy via your preferred CI/CD or zip deploy.

## Auth flow (OTP)
- Start: POST `/api/auth/start` with `{ userName, destination }`
- Verify: POST `/api/auth/verify` with `{ userName, code }`
- Logout: POST `/api/auth/logout`
- Auth ping: GET `/api/auth/me` returns minimal profile when authenticated

The SPA avoids page reloads: after verify/logout the client refreshes the Knockout view model and SignalR connection in place.

## Create Cosmos containers (Azure CLI)
```sh
# Database
az cosmosdb sql database create \
  --account-name $cosmosAccountName \
  --resource-group $resourceGroup \
  --name chat

# Users container (partition /userName)
az cosmosdb sql container create \
  --account-name $cosmosAccountName \
  --resource-group $resourceGroup \
  --database-name chat \
  --name users \
  --partition-key-path /userName

# Rooms container (partition /name)
az cosmosdb sql container create \
  --account-name $cosmosAccountName \
  --resource-group $resourceGroup \
  --database-name chat \
  --name rooms \
  --partition-key-path /name

# Messages container (partition /roomName) with default TTL 604800 (7 days)
az cosmosdb sql container create \
  --account-name $cosmosAccountName \
  --resource-group $resourceGroup \
  --database-name chat \
  --name messages \
  --partition-key-path /roomName \
  --default-ttl 604800
```
