# SignalR-Chat
Modern real-time chat on .NET 9 using SignalR (hub-only), Redis (OTP), optional Azure Communication Services (OTP delivery), OpenTelemetry instrumentation, and a lightweight vanilla JavaScript client. Cosmos / Azure SignalR are optional future scale-out paths; current implementation runs in-process with EF-based persistence.

## Features
* Multi-room chat (static rooms: general, ops, random)
* Text-only messages
* Optimistic message sending with correlation IDs
* Incremental pagination (newest first + older on scroll-up)
* Client-side rate limiting for sends
* Avatar initials with defensive refresh logic
* OTP-based authentication (cookie session) backed by Redis
  * Predefined demo users selectable from dropdown (alice, bob, charlie)
  * Console fallback for OTP delivery; ACS (Email/SMS) optional
  * Configurable send timeout, retry cooldown, resend support, progress indicator
* Structured telemetry: OpenTelemetry (traces, metrics, logs) + correlation header propagation
* Health endpoints: `/healthz` (readiness) and `/healthz/metrics` (light JSON counters)
* Reconnect backoff telemetry
* Immutable message log (no edits/deletes); static room topology
* Private messaging removed (room-only broadcast model)

## Rooms (Fixed Topology)
Rooms are a fixed, immutable set (`general`, `ops`, `random`). Interface:
```
IEnumerable<Room> GetAll();
Room? GetById(string id);
Room? GetByName(string name);
```
No runtime create/update/delete methods exist. Seeding ensures expected rooms and demo users if the store is empty.

## Architecture Overview
**Runtime**: ASP.NET Core 9 (Razor Pages + minimal APIs + SignalR Hub)

**Real-time**: In-process SignalR hub (no backplane yet). All message creation flows through hub methods—there is no REST send endpoint.

**Persistence**: EF Core (`ApplicationDbContext`) for users, rooms, messages (relational). Partitioning strategy conceptually groups messages per room.

**OTP / Cache**: Redis storing short-lived OTP codes (`otp:{user}` keys).

**Auth Flow**: OTP start → code stored in Redis → verify → auth cookie → establish hub connection.

**Observability**:
* OpenTelemetry Activities around hub & repository operations
* Optional OTLP / console exporters (selection logic in startup)
* Structured logs enriched with trace IDs
* Custom counters (messages sent, rooms joined, reconnect attempts, OTP events, active connections)

**Frontend**: Vanilla JS modules (`chat.js`, `site.js`) + esbuild-generated minified bundles under `wwwroot/js/dist/`. Only minified files are referenced by the pages.

## Local Development
Prerequisites:
* .NET 9 SDK
* Redis instance (local or hosted)
* (Optional) ACS for real OTP delivery

### Frontend Build
`esbuild` bundles `chat.js` and `site.js` into `wwwroot/js/dist/`. Edit the source files, then run the bundle task.

Core npm scripts (see `package.json`):
* `bundle:js` – build minified JS
* `build:css` / `build:css:prod` – Sass to CSS
* `build:prod` – full asset pipeline

VS Code tasks orchestrate: install → bundle → dotnet build.

### Run
```
dotnet run --project src/Chat.Web/Chat.Web.csproj --urls=http://localhost:5099
```
Open http://localhost:5099

## OTP Authentication (Summary)
1. Select demo user and initiate send
2. Code stored in Redis with TTL
3. Enter code, verify → cookie issued
4. Hub connection established → auto-join room → flush queued messages

Timeouts, resend cooldown, and telemetry instrumentation are all client-managed with clear UI states.

## Messaging Flow
1. User composes & sends → client assigns correlationId and renders optimistic message
2. Hub method invoked; server persists & broadcasts canonical DTO with correlationId
3. Client reconciles optimistic entry (removes temporary state)
4. Disconnected messages queue in sessionStorage until reconnection & join

## Scaling (Future)
| Concern | Current | Path |
|---------|---------|------|
| Real-time scale | Single hub instance | Add Redis/Azure SignalR backplane |
| Persistence | Single DB | Shard / partition store |
| OTP store | Single Redis | Managed or clustered Redis |
| Presence | Not implemented | Redis sets / ephemeral keys |
| Outbox durability | sessionStorage | IndexedDB or server persistence |

## Security Notes
* OTP codes stored plaintext (hashing recommended for stronger threat model)
* Rate limiting (OTP, sends) can be added using built-in ASP.NET rate limiting + Redis counters
* Correlation IDs are random UUIDs (no sensitive data)

## Telemetry Overview
Custom metrics (meter `Chat.Web`):
* `chat.messages.sent`
* `chat.rooms.joined`
* `chat.otp.requests`
* `chat.otp.verifications`
* `chat.connections.active`
* `chat.reconnect.attempts`

Client telemetry emits join attempts, send failures, queue flush metrics, reconnect attempt metadata.

## Health
`/healthz` basic readiness; `/healthz/metrics` lightweight JSON counters + uptime snapshot.

## Development Workflow Tips
* Always edit source (`wwwroot/js/chat.js`, `site.js`) not files in `dist/`
* Re-bundle after JS changes before refreshing browser
* Use a watch script if iterating rapidly (add `--watch` to esbuild locally)

## Roadmap
1. Backplane (Redis or Azure SignalR) for horizontal scale
2. Presence + typing indicators
3. Hash OTP codes & rate limiting policies
4. OTLP exporter hardened for prod + dashboards
5. CI guard for forbidden patterns (e.g., reintroduction of private messaging)
6. Enhanced pagination UX (skeleton loading / virtualization)

## License
See `LICENSE` file.
