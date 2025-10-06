# SignalR-Chat
Modern real-time chat on .NET 9 using Azure SignalR, Cosmos DB, Redis (OTP + state), optional Azure Communication Services (OTP delivery), Serilog + OpenTelemetry for observability, and a lightweight vanilla JavaScript client (KnockoutJS fully removed).


## Features
* Multi-room chat (predefined static rooms: general, ops, random)
* Text-only messages (concise scope, no file upload or custom emoji assets)
* Optimistic message sending with client-generated correlation IDs (prevents duplicates when server echoes)
* Incremental, scroll-triggered pagination (newest first load + "load older" on scroll-up)
* Client-side rate limiting for sends
* Avatar initials with defensive refresh logic
* OTP-based authentication (cookie session) backed by Redis
  * Predefined demo users selectable from dropdown (alice, bob, charlie)
  * Multi-channel delivery (Email + SMS) if Azure Communication Services configured; console fallback otherwise
  * Configurable send timeout, retry cooldown, resend support, progress indicator with live countdown
* Automatic Cosmos DB database & container creation (if allowed) + validation of presence of static rooms (`general`, `ops`, `random`). In Cosmos mode those rooms must be provisioned up‑front (migration/script/IaC). Sample users (`alice`, `bob`, `charlie`) are seeded only if user store is empty.
* Tri-signal observability: OpenTelemetry Traces + Metrics + Logs (runtime + custom domain metrics)
* Structured logging (Serilog) + manual OpenTelemetry spans (Activities) + correlation header propagation
* Health endpoints: `/healthz` (readiness) and `/healthz/metrics` (JSON in-process counters & uptime)
* Client reconnect backoff telemetry (manual spans + counter for reconnect attempts)
* Dynamic room creation/rename/delete removed (simpler, fixed topology)
* Delete message feature removed (simpler, immutable chat log)

## Rooms Repository (Read‑Only)
Rooms are a fixed, immutable set (`general`, `ops`, `random`). The `IRoomsRepository` surface area is intentionally minimal and query‑only:

```
IEnumerable<Room> GetAll();
Room? GetById(string id);
Room? GetByName(string name);
```

There are deliberately no create/update/delete methods. Corresponding HTTP endpoints for dynamic room creation or deletion return `410 Gone` so older clients fail fast and clearly. Room metadata (ids, names) must be established outside the running app (e.g. infrastructure provisioning). The in‑memory test implementation seeds them internally for convenience; production (Cosmos) simply reads them.

The startup `DataSeedHostedService` now only:
* Verifies the expected static room names exist (logs a warning if any are missing, but does not create or mutate rooms)
* Seeds demo users exactly once when the user container is empty

This keeps room topology truly immutable at runtime.

## Architecture Overview
**Runtime**: ASP.NET Core 9 (Razor Pages host + MVC API + SignalR Hub)

**Real-time Transport**: Azure SignalR Service (always on; no in-memory fallback).

**Data / Storage** (Cosmos DB SQL API):
| Container | Partition Key | Purpose |
|-----------|---------------|---------|
| users     | /userName     | User profiles / presence |
| rooms     | /name         | Room metadata |
| messages  | /roomName     | Chat messages (TTL recommended) |

**Caching / OTP**: Redis (stores short-lived OTP codes and potentially ephemeral state).

**Auth Flow**: Stateless OTP initiation → Redis-backed code → verify → issue auth cookie.

**Observability**:
* OpenTelemetry Traces, Metrics & Logs (single resource: `service.name=Chat.Web`, dynamic `service.version`)
* Serilog request logging (enriched with trace id when available) feeding console / platform logs
* Manual ActivitySource spans for hub operations, repository actions, reconnect attempts, and OTP flows
* Exporter auto-selection (Azure Monitor → OTLP → Console) applied uniformly to traces, metrics, and logs
* Custom domain metrics (see Metrics section) + runtime/ASP.NET/SignalR/Rate Limiting instrumentation

**Frontend**: Single vanilla JS module (`wwwroot/js/chat.js`) manages:
* State store (rooms, users, messages, pending optimistic messages)
* Pagination requests (`take`, `before` timestamp)
* SignalR connection + reconnection/backoff
* Correlation-based optimistic reconciliation
* DOM rendering without frameworks (all previous Knockout bindings removed)

**Simplifications**:
* KnockoutJS removed (vanilla JS only)
* No file attachment pipeline (lean chat core)
* Immutable messages (no delete/edit)
* Static room list (no runtime create/delete)

## Local Development
Prerequisites:
* .NET 9 SDK
* Azure resources (or local emulators for Cosmos / Redis if you adapt config)

### Frontend Assets Pipeline
Assets are now managed entirely via npm (LibMan removed).

Sass (`site.scss` + partials) compiled with the modern module system (`@use`). Namespacing is applied; no wildcard `as *`.

Key scripts:
* `npm run build:css` – development CSS (unminified) to `wwwroot/css/site.css`
* `npm run build:css:prod` – compressed CSS to `wwwroot/css/site.min.css`
* `npm run watch:css` – watch mode
* `npm run copy:libs` – copies `bootstrap` and `@microsoft/signalr` distributables into `wwwroot/lib`
* `npm run build` – full pipeline (dev + prod css + library copy)

Runtime uses `site.css`; you can switch to the minified file in `_Layout.cshtml` if desired or add an environment-specific tag helper.

JS Bundling: `esbuild` bundles `site.js` and `chat.js` for production via `npm run bundle:js` (invoked by `npm run build:prod`). Dev environment serves original unminified sources; production serves files from `wwwroot/js/dist/`.

Removed legacy libraries: jQuery, Knockout, jquery-validation.* — vanilla JS + Bootstrap events are sufficient.

To add more libraries: `npm i <pkg>` then either extend `scripts/copy-libs.cjs` (for direct file copies) or import them in a bundled entrypoint and rebuild.

### 1. Environment Configuration
You can use environment variables (recommended) or a `.env.local` loaded by your shell. Required keys:

| Purpose | Key | Example |
|---------|-----|---------|
| Cosmos connection | `Cosmos__ConnectionString` | `AccountEndpoint=...;AccountKey=...;` |
| Cosmos DB name | `Cosmos__Database` | `chat` |
| Cosmos containers | `Cosmos__UsersContainer` | `users` |
|  | `Cosmos__RoomsContainer` | `rooms` |
|  | `Cosmos__MessagesContainer` | `messages` |
| Redis cache | `Redis__ConnectionString` | `myredis.redis.cache.windows.net:6380,...` |
| (Optional) Azure SignalR | `Azure__SignalR__ConnectionString` | `Endpoint=...;AccessKey=...;Version=1.0;` |
| (Optional) ACS | `Acs__ConnectionString` | `endpoint=...;accesskey=...` |
| (Optional) ACS email sender | `Acs__EmailFrom` | `no-reply@yourdomain` |
| (Optional) ACS SMS sender | `Acs__SmsFrom` | `+10000000000` |

Placeholders in `appsettings.json` intentionally DO NOT contain secrets—real values must come from env/user-secrets.

### 2. Running
```bash
dotnet run --project src/Chat.Web/Chat.Web.csproj --urls=http://localhost:5099
```
Then visit: http://localhost:5099

### 3. OTP Authentication Flow (Updated)
Current UX removes free‑text destination entry and replaces it with a secure, deterministic demo user selection.

1. Open Sign In → pick a demo user from the dropdown (users are pre-seeded with Email + Mobile).
2. Click "Send code" (or press Enter after focusing the button) – the sending indicator appears with a live seconds countdown (configured by `data-otp-timeout-ms`, default 8s).
3. On success, indicator shows "Sent to email & mobile" then fades; Step 2 (code entry) is revealed and the code input focused.
4. Enter the 6‑digit code (or press Enter to submit). Successful verification logs latency telemetry and closes the modal.
5. If delivery fails, an error state with a disabled Retry link appears. Retry re-enables after a cooldown (configured by `data-otp-retry-cooldown-ms`, default 5s) and shows a countdown.
6. While on Step 2 you can use the "Resend" link to trigger a new (or reused, if still valid) code without leaving the code entry step.

Additional Behavior:
* Timeout: If the send does not complete before timeout, the request is aborted; telemetry logs a timeout event.
* Resend: Maintains focus in Step 2; only the sending indicator changes state.
* Abort: Closing the modal aborts any in-flight send and resets all timers, countdowns, and disabled states.
* Telemetry: Each send is assigned a `sendId`; we log duration (`OTP code sent` + `durationMs`) and subsequent verification latency (`OTP verify latency`) correlated by `sendId`.
* No destination field: Destination is implicit from seeded user record (Email + MobileNumber) promoting consistency and preventing enumeration attacks in this demo context.

Configurable via Markup Data Attributes (on `#otpContainer`):
| Attribute | Purpose | Default |
|-----------|---------|---------|
| `data-otp-timeout-ms` | Milliseconds before send aborts | 8000 |
| `data-otp-retry-cooldown-ms` | Milliseconds before retry becomes active after a failure | 5000 |

Client Elements of Interest:
| Element | Role |
|---------|------|
| `#btn-send-otp` | Initiates first send (enters Step 2 on success) |
| `#btn-resend-otp` | Issues subsequent sends from Step 2 (stays in Step 2) |
| `#otpSendingIndicator` | Houses sending / success / error states |
| `#otpSendCountdown` | Displays remaining seconds until timeout |
| `#otpRetryLink` | Retry action (disabled during cooldown) |
| `#otpRetryCountdown` | Shows remaining cooldown seconds |

Keyboard Shortcuts:
* Enter in code field → Verify
* Enter on Step 1 with focus on Send → Send code

Failure Modes & Recovery:
| Scenario | UI Result | Recovery |
|----------|-----------|----------|
| Network timeout | Indicator hides (timeout logged), error state with cooldown | Wait for retry cooldown or close modal |
| Abort (modal closed) | Indicator fades out immediately | Reopen modal, select user again |
| Server error (non-2xx) | Error state + retry cooldown | Retry after cooldown |
| Rapid double click | Ignored (button disabled while in-flight) | N/A |

### 4. Pagination & Optimistic Messaging
* Initial load fetches newest N messages ascending.
* Scrolling upward triggers older batch fetch using `before=<ISO timestamp>`.
* Messages you send appear immediately with a temporary (negative/client) ID and correlation ID; replaced upon server broadcast.

### 5. Troubleshooting
| Symptom | Hint |
|---------|------|
| Startup exception: missing Cosmos | Verify `Cosmos__ConnectionString` not placeholder & reachable |
| Redis connection error | Ensure SSL flag & port 6380 for Azure; 6379 locally |
| Duplicate messages on slow network | Check correlation ID reconciliation logic still intact in `chat.js` |
| No OTP delivered | ACS not configured → check console output |

### 6. Optional Emulator (Advanced)
You can adapt code to allow local Cosmos emulator + local Redis (not enabled by default). Add detection logic or env flags if desired.

## Production / Deployment Notes
Provision (Azure recommended):
* Azure SignalR Service
* Azure Cosmos DB (SQL API) with the three containers
* Azure Cache for Redis
* (Optional) Azure Communication Services (Email/SMS) for OTP

Environment / App Settings (Production):
| Key | Notes |
|-----|-------|
| `Azure:SignalR:ConnectionString` or `Azure__SignalR__ConnectionString` | Required for SignalR scale |
| `Cosmos:ConnectionString` | Mandatory, no placeholder |
| `Redis:ConnectionString` | Mandatory |
| `Acs:*` | Optional (enables real OTP delivery) |
| `ASPNETCORE_ENVIRONMENT=Production` | Enables HSTS, production logging profile |

Consider disabling automatic container creation in prod (if previously enabled) to avoid schema drift.

## Azure App Service Deployment (PaaS)
This project is designed for classic *non-container* Azure App Service deployment (Windows or Linux) using the platform build + runtime.

### 1. Prerequisites
* Resource Group
* Azure SignalR Service (Standard or Free for dev)
* Azure Cosmos DB (SQL API) account
* Azure Cache for Redis (Basic/Standard)
* (Optional) Azure Communication Services (Email/SMS)
* App Service Plan + App Service (Runtime: .NET 9)

### 2. Create Resources (CLI excerpt)
```bash
az group create -n chat-rg -l westeurope
az signalr create -n chat-svc -g chat-rg --sku Free_F1
az cosmosdb create -n chatcosmos -g chat-rg --kind GlobalDocumentDB
az redis create -n chatredis -g chat-rg --sku Basic --vm-size C0
# (Optional) ACS: via portal or az communication (depends on region availability)
az webapp create -n chat-web -g chat-rg -p chat-plan --runtime "DOTNET:9" \
  --deployment-local-git
```

### 3. Configure App Settings
In the Web App (Portal → Configuration or CLI):

| Setting | Value (example) |
|---------|-----------------|
| `Azure:SignalR:ConnectionString` | (copy from SignalR Keys) |
| `Cosmos:ConnectionString` | (Primary connection string) |
| `Cosmos:Database` | chat |
| `Cosmos:UsersContainer` | users |
| `Cosmos:RoomsContainer` | rooms |
| `Cosmos:MessagesContainer` | messages |
| `Redis:ConnectionString` | (Primary Redis connection string) |
| `Acs:ConnectionString` | (Optional) |
| `Acs:EmailFrom` | (Optional) |
| `Acs:SmsFrom` | (Optional) |
| `ASPNETCORE_ENVIRONMENT` | Production |

> Do **NOT** store secrets in `appsettings.json` – use App Settings (they become environment variables) or Key Vault references.

### 4. Build & Deploy (Local Git or Zip)
Option A – Local Git (after `az webapp create` with local git deployment):
```bash
git remote add azure $(az webapp deployment source config-local-git -n chat-web -g chat-rg --query url -o tsv)
git push azure main
```

Option B – Zip Deploy:
```bash
dotnet publish src/Chat.Web/Chat.Web.csproj -c Release -o publish
cd publish
zip -r ../chat.zip .
cd ..
az webapp deploy -g chat-rg -n chat-web --src-path chat.zip --type zip
```

### 5. First Run / Warm Up
* App will validate required settings (fails fast if placeholders remain)
* Optional seeding of containers & sample data if code path enabled
* Check `/healthz` for `ok`

### 6. Logging & Diagnostics
* Enable App Service Logs (File System / Diagnostics) for quick troubleshooting
* Stream logs: `az webapp log tail -n chat-web -g chat-rg`
* Consider adding OTLP exporter + Azure Monitor / Application Insights for production

### 7. Scaling Guidance
* Scale out: Use higher SignalR Service SKU & App Service horizontal scale
* Cosmos RU: allocate based on write/read volume; messages partitioned by room
* Redis: scale tier with OTP throughput & expiry

### 8. Recommended Hardening
* Configure Key Vault and replace connection strings with Key Vault references in App Settings
* Enforce HTTPS only; already enabled by platform
* Add WAF (Front Door / App Gateway) if external exposure requires enhanced protection
* Add rate limiting at hub/API (e.g., AspNetCore rate limiting middleware) for abusive traffic mitigation

### 9. CI/CD (GitHub Actions outline)
1. Trigger on push to `main`
2. `actions/setup-dotnet` (version 9.x)
3. `dotnet restore`, `dotnet build --configuration Release`, `dotnet test` (once tests added)
4. `dotnet publish` to artifact folder
5. Deploy using `azure/webapps-deploy@v2` with publish profile secret

> For Infrastructure as Code you can introduce Bicep later (placing files under an `infra/` folder) without changing the application deployment model.


### Observability Extension
The application now emits tri-signal telemetry (traces, metrics, logs) via OpenTelemetry. A single exporter choice applies to all three signals.

Exporter Priority (evaluated at startup):
1. Production environment + Application Insights connection string present → Azure Monitor exporter (traces, metrics, logs)
2. Else if `OTel:OtlpEndpoint` configured → OTLP gRPC exporter
3. Else → Console exporter (human-readable spans, metric dumps, structured log lines)

Minimal local OTLP example (Tempo / Collector): set env var `OTel__OtlpEndpoint=http://localhost:4317` and run an OpenTelemetry Collector.

Azure Monitor requires the connection string only; no explicit instrumentation key code path is needed.

### Metrics (Custom + Runtime)
Custom domain metrics (Meter `Chat.Web`):
| Metric | Type | Description |
|--------|------|-------------|
| `chat.messages.sent` | Counter | Messages broadcast by server (post persistence/broadcast) |
| `chat.rooms.joined` | Counter | Successful room join operations (excludes duplicates) |
| `chat.otp.requests` | Counter | OTP start requests accepted (pre-rate-limit rejection) |
| `chat.otp.verifications` | Counter | Successful OTP verifications |
| `chat.connections.active` | Up/Down Counter | Live SignalR connections (increment on connect, decrement on disconnect) |
| `chat.reconnect.attempts` | Counter | Client-driven reconnect attempts (backoff sequence) |

Additional automatically collected metrics include (non-exhaustive):
* `http.server.request.duration`, `http.server.active_requests`
* `signalr.server.active_connections`, `signalr.server.connection.duration`
* Rate limiting (`aspnetcore.rate_limiting.*`)
* Runtime (`process.runtime.dotnet.*`, GC/JIT/thread pool)

## Changes: Fixed Room Membership & Removed Private Messages
Users are now predefined with immutable room assignments stored in the user profile (Email, MobileNumber, FixedRooms). The hub enforces authorization: attempts to join a non-assigned room are rejected. Legacy private messaging (/private(user) ...) has been removed client & server side; the connection map remains for future targeted notifications (e.g., alerts, system pings).

### New User Fields
- Email
- MobileNumber
- FixedRooms (array of allowed room names)

### Updated Seeding
On first run (if no users) the seeder creates rooms: general, ops, random and users:
- alice (general, ops)
- bob (general, random)
- charlie (general)

### Presence & Availability Metrics
Added metrics published via OpenTelemetry:
- chat.room.presence (UpDownCounter per room tag)
- chat.user.availability.events (Counter: state=online/offline, device=<device>)
- Existing counters (messages, joins, active connections, reconnect attempts) unaffected.

### Logs
* Serilog handles structured application logs.
* OpenTelemetry logging provider forwards enriched log records through the same exporter selection.
* Trace correlation: log records carry trace/span ids enabling end-to-end joins in backends.

### Reconnect Telemetry
Client reconnection logic (exponential backoff) posts to `/api/telemetry/reconnect` with attempt index & planned delay plus classified failure information. Server creates an Activity (`Client.ReconnectAttempt`) and increments `chat.reconnect.attempts`.

Tags added to each reconnect span:
| Tag | Example | Notes |
|-----|---------|-------|
| `reconnect.attempt` | `3` | 1-based attempt counter |
| `reconnect.delay_ms` | `8000` | Planned delay before next attempt |
| `reconnect.error.category` | `transport` | One of: `auth`, `timeout`, `transport`, `server`, `other`, `unknown` |
| `reconnect.error.message` | `WebSocket closed with status code 1006` | Truncated to 180 chars server-side |

Error category is heuristically derived from exception text on the client (e.g. network keywords → `transport`, 401/403 → `auth`). This provides quick aggregation capability (e.g. chart reconnects by category) without central parsing.

### `/healthz/metrics`
Lightweight JSON snapshot (in-process counters + uptime) intended for dashboards / quick health probes without scraping full OTLP metrics pipeline.

Sample (fields abbreviated):
```json
{
  "uptimeSeconds": 1234.56,
  "messagesSent": 42,
  "roomsJoined": 7,
  "otpRequests": 5,
  "otpVerifications": 5,
  "reconnectAttempts": 3,
  "activeConnections": 0
}
```

### Scaling
* SignalR scale handled by Azure SignalR Service SKU.
* Cosmos RU throughput: size based on message ingestion & query pattern (recent + historical by room partition).
* Redis tier sized by OTP volume and expiration policy.

## API (Selected Endpoints)
| Purpose | Method | Path | Notes |
|---------|--------|------|-------|
| Start OTP | POST | `/api/auth/start` | Body: `{ userName }` (delivery inferred from profile; existing unexpired code may be reused) |
| Verify OTP | POST | `/api/auth/verify` | Provide `userName`, `code` |
| List OTP users | GET | `/api/auth/users` | Returns predefined selectable users |
| Logout | POST | `/api/auth/logout` | Clears auth cookie |
| Current user | GET | `/api/auth/me` | Returns basic profile |
| Recent messages | GET | `/api/Messages/Room/{room}?take=50` | Newest ascending |
| Older messages | GET | `/api/Messages/Room/{room}?before=ISO&take=50` | Paged history |
| Send message | POST | `/api/Messages` | Includes optional `CorrelationId` |
| Rooms list | GET | `/api/Rooms` | Static predefined rooms |
| Health metrics snapshot | GET | `/healthz/metrics` | In-process counters + uptime |
| Reconnect telemetry (internal) | POST | `/api/telemetry/reconnect` | Client reconnect attempt (not user-facing) |

Message deletion intentionally not supported.

## Create Cosmos Containers Manually (CLI)
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
  --ttl 604800
```

## Observability Details
* Each incoming HTTP request: wrapped in an Activity; trace id optionally surfaced via `X-Trace-Id` header.
* Repository operations add tags (room name, counts) for later correlation.
* Serilog enrichers add environment, process, thread information.
* OTLP Export: Set `OTel:OtlpEndpoint` (e.g. `http://localhost:4317`) to automatically switch from console exporter to OTLP (Jaeger / Tempo / Azure Monitor gateway). Fallback is console.

### Exporter Selection Logic
The application chooses ONE trace exporter at startup in this priority order:

1. Production + Application Insights connection string present → Azure Monitor (Application Insights) exporter
2. Otherwise, if `OTel:OtlpEndpoint` configured → OTLP exporter
3. Otherwise → Console exporter (human-readable spans for local dev)

Detection Rules:
* Production environment is determined by `ASPNETCORE_ENVIRONMENT=Production`.
* Application Insights connection string can be supplied either as configuration key `ApplicationInsights:ConnectionString` or environment variable `APPLICATIONINSIGHTS_CONNECTION_STRING`.

Configuration Keys / Env Vars:
| Purpose | Key | Example |
|---------|-----|---------|
| App Insights Connection | `ApplicationInsights:ConnectionString` | `InstrumentationKey=...;IngestionEndpoint=https://...` |
| (Alt env var) App Insights | `APPLICATIONINSIGHTS_CONNECTION_STRING` | (same format) |
| OTLP Endpoint | `OTel:OtlpEndpoint` | `http://localhost:4317` |

Service metadata includes `service.name=Chat.Web` and `service.version` (derived from assembly) via OpenTelemetry Resource attributes.

### Local Development Recommendations
* For Jaeger or Tempo locally, run the collector and export by setting `OTel:OtlpEndpoint`.
* Leave both AI connection string unset and you will see console spans—fast feedback without external dependencies.

### Enabling Application Insights
1. Create an Application Insights resource (Workspace-based).
2. Copy the Connection String.
3. Set `ASPNETCORE_ENVIRONMENT=Production` (on Azure App Service this is typically already set) and add either:
  * `ApplicationInsights:ConnectionString` in configuration
  * or env var `APPLICATIONINSIGHTS_CONNECTION_STRING`
4. Restart the app—new traces will flow to AI (requests, custom Activities, rate limited 429s, etc.).

## Integration Tests
Added lifecycle integration tests (xUnit):
* Duplicate connection resilience
* Disconnect without room
* Join → Leave cycle
* Room switching (previous room leave + new join)
* Double disconnect idempotency

Test project: `tests/Chat.IntegrationTests` uses `WebApplicationFactory` and header auth.

## Configuration Strategy
* `appsettings.json` holds non-secret structural defaults (placeholders).
* Environment-specific overrides: `appsettings.Production.json` (stricter logging). Development may rely on user secrets / `.env.local`.
* Real secrets ALWAYS via environment variables or user secrets — avoid committing to repo.

### Sample `.env.local.example`
```
# Cosmos
Cosmos__ConnectionString="AccountEndpoint=https://<account>.documents.azure.com:443/;AccountKey=<KEY>;"
Cosmos__Database=chat
Cosmos__UsersContainer=users
Cosmos__RoomsContainer=rooms
Cosmos__MessagesContainer=messages

# Redis
Redis__ConnectionString=<your redis connection string>

# (Optional) Azure SignalR
# Azure__SignalR__ConnectionString=Endpoint=https://<name>.service.signalr.net;AccessKey=<KEY>;Version=1.0;

# (Optional) Azure Communication Services (OTP delivery)
# Acs__ConnectionString=endpoint=https://<resource>.communication.azure.com/;accesskey=<KEY>
# Acs__EmailFrom=no-reply@yourdomain
# Acs__SmsFrom=+10000000000
```

## Roadmap / Ideas
* Add OTLP exporter + tracing dashboard
* Introduce integration tests for optimistic reconciliation & pagination
* Optional in-memory dev mode (no Cosmos/Redis) for rapid prototyping
* UI refinement for pending (optimistic) messages (e.g., opacity until
