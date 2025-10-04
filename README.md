# SignalR-Chat
Modern real-time chat on .NET 9 using Azure SignalR, Cosmos DB, Redis (OTP + state), optional Azure Communication Services (OTP delivery), Serilog + OpenTelemetry for observability, and a lightweight vanilla JavaScript client (KnockoutJS fully removed).


## Features
* Multi-room chat (create / switch rooms)
* (Optional) simple private whisper syntax: `/private(Name) message` (server relays if enabled)
* Text-only messages (concise scope, no file upload or custom emoji assets)
* Optimistic message sending with client-generated correlation IDs (prevents duplicates when server echoes)
* Incremental, scroll-triggered pagination (newest first load + "load older" on scroll-up)
* Client-side rate limiting for sends
* Avatar initials with defensive refresh logic
* OTP-based authentication (cookie session) backed by Redis
  * Azure Communication Services (Email/SMS) if configured; console fallback otherwise
* Automatic Cosmos DB database & container creation (if allowed) + seeding default `general` room & sample users (`alice`, `bob`)
* Tri-signal observability: OpenTelemetry Traces + Metrics + Logs (runtime + custom domain metrics)
* Structured logging (Serilog) + manual OpenTelemetry spans (Activities) + correlation header propagation
* Health endpoints: `/healthz` (readiness) and `/healthz/metrics` (JSON in-process counters & uptime)
* Client reconnect backoff telemetry (manual spans + counter for reconnect attempts)
* Delete message feature removed (simpler, immutable chat log)

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

### 3. OTP Authentication Flow
1. Enter username (existing or new) and destination (email / phone depending on ACS configuration or console fallback).
2. Enter code received (or check console output if no ACS configured).
3. Session cookie established; SignalR connection re-initialized with identity.

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
| Start OTP | POST | `/api/auth/start` | Provide `userName`, `destination` (email / phone) |
| Verify OTP | POST | `/api/auth/verify` | Provide `userName`, `code` |
| Logout | POST | `/api/auth/logout` | Clears auth cookie |
| Current user | GET | `/api/auth/me` | Returns basic profile |
| Recent messages | GET | `/api/Messages/Room/{room}?take=50` | Newest ascending |
| Older messages | GET | `/api/Messages/Room/{room}?before=ISO&take=50` | Paged history |
| Send message | POST | `/api/Messages` | Includes optional `CorrelationId` |
| Rooms list | GET | `/api/Rooms` | Basic metadata |
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

### Logs
Currently Serilog writes structured logs to console (App Service captures them). You can later add `Serilog.Sinks.ApplicationInsights` or OTel log exporter if central log correlation is required.

### Trace Header Propagation
Custom middleware adds `X-Trace-Id` and `X-Span-Id` if the response has not yet started. These can be forwarded by reverse proxies for cross-hop correlation.

### Hub Spans
Custom spans created for: `ChatHub.OnConnected`, `ChatHub.OnDisconnected`, `ChatHub.Join`, `ChatHub.Leave`. Duplicate connection detection sets tag `chat.duplicateConnection=true`.

## Rate Limiting
ASP.NET Core built-in fixed window limiter applied ONLY to sensitive OTP endpoints:
* `POST /api/auth/start`
* `POST /api/auth/verify`

Default Policy (production/dev defaults): 5 requests per 60 seconds per IP, queue length 0 (excess immediately rejected with 429).

Configurable Keys:
| Setting | Key | Default |
|---------|-----|---------|
| Permit limit | `RateLimiting:Auth:PermitLimit` | 5 |
| Window seconds | `RateLimiting:Auth:WindowSeconds` | 60 |
| Queue limit | `RateLimiting:Auth:QueueLimit` | 0 |

Integration tests override these to a shorter window (5 seconds) for fast deterministic validation by injecting in-memory configuration values.

## In-Memory Test Mode
Integration tests enable an in-memory mode (`Testing:InMemory=true`) that swaps:
* Cosmos repositories → in-memory collections
* Redis OTP store → in-memory dictionary
* Azure SignalR → in-process SignalR
* Authentication → custom header-based test handler (`X-Test-User`)

This keeps tests hermetic with no external dependencies. Enable manually by setting env var `Testing__InMemory=true` for local experimentation.

## File Structure (Key Paths)
```
src/Chat.Web/
  Program.cs / Startup.cs          # Host + service wiring & OpenTelemetry
  Controllers/                     # REST + telemetry ingestion endpoints
  Hubs/ChatHub.cs                  # Real-time hub (presence, rooms, private send)
  Repositories/                    # Cosmos + InMemory data access abstractions
  Services/                        # OTP senders/stores, metrics, seeding, test auth
  Models/                          # Domain entities (User, Room, Message)
  ViewModels/                      # API/Hub DTO projections (MessageViewModel etc.)
  wwwroot/js/chat.js               # Vanilla JS client (state + SignalR + telemetry)
  wwwroot/css/                     # SCSS + compiled CSS
tests/ (if present)                # Integration tests (signalr lifecycle)
```

## Deletion Candidates (Approved)
| Item | Reason | Action |
|------|--------|--------|
| `UploadViewModel.cs` | Legacy file upload feature removed | Delete (safe) |
| Emoji picker assets (removed) | Already excised | None |
| Identity scaffolding (if reappears) | OTP-only auth | Remove if unused |
| Legacy comments (e.g. deleteMessage) | Clarity | Optional cleanup |

Keep this list in sync if features are reintroduced.

## Seeding Control
Data seeding now respects env flag:
```
Seeding__Enabled=false   # Skip seeding alice/bob + 'general' room
```
Omit or set to any other value to enable default seeding.

## Legacy / Removed Components
Removed to simplify maintenance & reduce surface area:
* KnockoutJS MVVM → replaced with small stateful module (`chat.js`).
* File uploads & emoji picker UI (server + static assets purged).
* Message deletion (immutability + simpler auditability).

## Adoption Checklist
1. Set Cosmos / Redis / SignalR secrets via environment.
2. (Optional) Provide OTLP endpoint or App Insights connection string.
3. Decide on seeding (`Seeding__Enabled=false` in production if you do not want sample data).
4. Verify rate limiting suits auth security requirements.
5. Deploy and monitor `/healthz` + traces/metrics for baseline.


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
* UI refinement for pending (optimistic) messages (e.g., opacity until acknowledged)

## License
See `LICENSE`.

---
Questions or suggestions? Open an issue or PR.
