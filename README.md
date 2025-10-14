# SignalR-Chat
Real-time multi-room chat on .NET 9 using SignalR (in‑process hub), EF Core persistence, Redis for OTP codes (or in‑memory when testing), optional Azure SignalR (configured automatically when not in test mode), OpenTelemetry (traces + metrics + logs) and a small vanilla JavaScript client. OTP codes are stored hashed by default using Argon2id with a per-code salt and an environment-supplied pepper.

The project intentionally keeps scope tight: fixed public rooms, text messages only, no editing/deleting, and OTP-based authentication.

## Implemented Features (Current State)
* Multi-room chat (fixed rooms: `general`, `ops`, `random`)
* Text messages only (immutable after send)
* Optimistic send with client correlation IDs and reconciliation
* Incremental pagination (newest batch first; fetch older on upward scroll)
* Client-side send pacing (basic rate limiting logic in JS)
* Avatar initials (derived client-side with cache bust/refresh protection)
* OTP authentication (cookie session)
  * Users: `alice`, `bob`, `charlie`
  * OTP code stored in Redis (or in-memory store under test flag)
  * Hashed storage by default (Argon2id + salt + pepper) with a versioned format
  * Console fallback delivery (ACS email/SMS supported only if configured)
* Connection & reconnect telemetry (duplicate start suppression + backoff attempts counter)
* OpenTelemetry traces + metrics + logs; custom counters for chat domain events
* Background-stable hub connection
  * Infinite reconnect policy with exponential backoff
  * Extended timeouts to tolerate background tab throttling (serverTimeout ~ 240s; keepAlive ~ 20s)
  * Proactive reconnect on tab visibility change and when the browser comes back online
* Attention cue: browser title blinking when a new message arrives while the tab is hidden (stops when visible)
* Health endpoints: `/healthz` (liveness), `/healthz/ready` (readiness), `/healthz/metrics` (lightweight snapshot)
* Outbox queue: pending messages buffered while disconnected and flushed after reconnect & room join
* Duplicate hub start guard (prevents false reconnect storms)
* SessionStorage backed optimistic message reconciliation

## Fixed Room Topology
Rooms are static; there is no runtime CRUD. The seeding hosted service ensures the three canonical rooms and initial users exist on startup.

## Architecture Overview
**Runtime**: ASP.NET Core 9 (Razor Pages + Controllers + SignalR Hub)  
**Real-time**: SignalR hub (Azure SignalR automatically added when not running in in-memory test mode)  
**Persistence**: EF Core context for users, rooms, messages (Cosmos repositories or in-memory depending on config)  
**OTP / Cache**: Redis (or in-memory fallback) storing short-lived OTP codes (`otp:{user}`)  
**Auth Flow**: Request code → store in OTP store → user enters code → cookie issued → hub connects  
**Observability**: OpenTelemetry (trace + metric + log providers) with exporter priority (Azure Monitor > OTLP > Console) and domain counters  
Serilog OTLP sink is enabled only when `OTel__OtlpEndpoint` is set to avoid startup issues in environments without an OTLP endpoint.  
**Frontend**: Source JS in `wwwroot/js/` referenced directly by pages (`site.js`, `chat.js`, `login.js`). Minified bundles in `wwwroot/js/dist/` are optional and not required for development—pages do not reference `dist` by default.

**Auth & Redirects**: Dedicated `/login` page issues a cookie after OTP verification. Redirect targets are validated on the server with `Url.IsLocalUrl`; the verify API returns a server-approved `nextUrl` used by the client.

### Cosmos messages retention (TTL)
If you run with Cosmos DB repositories enabled, the messages container can use a configurable TTL (time-to-live):

- Key: `Cosmos:MessagesTtlSeconds`
- Values:
  - Positive integer (seconds): items auto-expire after the given duration (e.g., `604800` for 7 days)
  - `-1`: TTL is enabled but items never expire by default (Cosmos semantics)
  - `null` or unset/empty: TTL is disabled entirely (container DefaultTimeToLive is cleared)
- Reconciliation: On startup, the app reconciles the container's `DefaultTimeToLive` to match the configured value, updating or clearing it as needed.

Environment variable examples (zsh):

```
# Disable TTL entirely
export Cosmos__MessagesTtlSeconds=
# or explicitly set the literal string "null"
export Cosmos__MessagesTtlSeconds=null

# Keep messages for 7 days
export Cosmos__MessagesTtlSeconds=604800

# Enable TTL but never expire by default
export Cosmos__MessagesTtlSeconds=-1
```

## Local Development
Prerequisites:
* .NET 9 SDK
* Redis (unless running with in-memory testing configuration)
* (Optional) Azure Communication Services connection (email/SMS) for real OTP delivery

### Build & Run (Manual)
```
dotnet build ./src/Chat.sln
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```
Navigate to: http://localhost:5099

### VS Code Tasks (Automation)
The workspace includes curated tasks (see `.vscode/tasks.json`). These are optional; the app runs without bundling because pages reference source JS. Key tasks:

| Task Label | Purpose |
|------------|---------|
| npm install | Install frontend dependencies (esbuild, sass). |
| bundle js (prod) | Optional: Build/minify JS + compile Sass (depends on npm install). |
| dotnet build | Compile the .NET solution. |
| build all | Full pipeline: npm install → bundle js (prod) → dotnet build. |
| test | Run solution tests (`dotnet test --no-build`). |
| Run Chat (Azure local env) | Load `.env.local` (if present), set Development environment, run app on http://localhost:5099. |
| PROD Run Chat (Azure local env) | Same as above but forces `ASPNETCORE_ENVIRONMENT=Production`. |

Recommended editing cycle:
1. Modify source JS (`wwwroot/js/*.js`) and/or Razor pages
2. Run `Run Chat (Azure local env)` task (or `dotnet run` as below)
3. Refresh browser

Note: If you choose to bundle for production testing, use the provided tasks. Dist files are generated but not referenced by default.

## OTP Authentication (Summary)
1. User selects identity and requests code
2. Code persisted with TTL in configured OTP store; when hashing is enabled, the stored value has the format `OtpHash:v2:argon2id:...`
3. User submits code; on success a cookie auth session is issued
4. Client starts (or reuses) hub connection; queued optimistic messages (if any) flush

Console output displays the OTP when ACS is not configured.

### Hashing details
- Algorithm: Argon2id (Isopoh.Cryptography.Argon2)
- Format: `OtpHash:v2:argon2id:m={KB},t={it},p={par}:{saltB64}:{encoded}`
- Preimage: `pepper || userName || ':' || salt || ':' || code`
- Configuration via `Otp` options:
  - `Otp:HashingEnabled` (default: true)
  - `Otp:MemoryKB` (default: 65536), `Otp:Iterations` (default: 3), `Otp:Parallelism` (default: 1), `Otp:OutputLength` (default: 32)
  - `Otp__Pepper` environment variable overrides `Otp:Pepper` and must be a Base64 string

Testing notes:
- Integration tests run with `Testing:InMemory=true` and a console OTP sender.
- The verify endpoint isn’t invoked by tests due to a known test harness middleware interaction; tests assert storage format via the DI-resolved `IOtpStore`.

## Messaging Flow
1. User sends → client assigns `correlationId`, renders optimistic message
2. Hub method persists & broadcasts canonical payload with same `correlationId`
3. Client reconciles optimistic entry (replaces temp rendering)
4. If offline/disconnected: message stored in sessionStorage outbox until reconnect & room join

### REST Fallback (Feature-Flagged)
A narrow REST POST endpoint (`POST /api/Messages`) exists solely to satisfy the
"immediate post after authentication" integration scenario and is **disabled by default**.
It can be enabled by setting configuration key `Features:EnableRestPostMessages=true` (tests do this via the custom factory).
In production the endpoint returns 404, encouraging clients to use only the SignalR hub path.

## Telemetry & Metrics
Exporter selection (in `Startup`) prioritizes: Azure Monitor (when Production + connection string) → OTLP endpoint → Console.

Custom counters (Meter `Chat.Web`):
* `chat.messages.sent`
* `chat.rooms.joined`
* `chat.otp.requests`
* `chat.otp.verifications`
* `chat.reconnect.attempts`

Client emits lightweight events for: reconnect attempts, duplicate start skips, message send outcomes, pagination fetches, queue flushes.
Server logs use Serilog; OTLP export is conditionally enabled only when `OTel__OtlpEndpoint` is configured.

## Security Notes
* OTP codes are stored hashed by default (Argon2id + salt + pepper). To support legacy/testing scenarios, plaintext storage can be toggled with `Otp:HashingEnabled=false`.
* Provide a high-entropy Base64 pepper via `Otp__Pepper` in each environment. Keep this secret out of source control.
* Rate limiting applied to auth/OTP endpoints via fixed window limiter (configurable limits).
* Redirect safety: server validates `ReturnUrl` with `Url.IsLocalUrl` and responds with a server-issued `nextUrl`; the client uses that value. Client also performs a basic path check as a secondary guard.
* DOM XSS hardening: client code avoids `innerHTML` when rendering user-controlled content (uses `textContent` and element creation).
* Log forging mitigation: request method and path are sanitized before logging in `RequestTracingMiddleware`.
* Correlation IDs are random UUIDs (no sensitive data embedded).

## SignalR Authentication Model
- The chat hub (`Hubs/ChatHub.cs`) is decorated with `[Authorize]`; anonymous clients cannot connect.
- In normal (non-testing) mode the app uses cookie authentication:
  - Configured in `Startup.cs` via `AddAuthentication().AddCookie(...)` with Sliding Expiration and a 12h `ExpireTimeSpan`.
  - The browser automatically sends the auth cookie during SignalR negotiate and WebSocket upgrade requests.
- In testing (`Testing:InMemory=true`) a simple test auth scheme is used, but the hub still requires an authenticated principal.
- The client connects with:
  - `new signalR.HubConnectionBuilder().withUrl('/chatHub').withAutomaticReconnect(...).build()` (see `wwwroot/js/chat.js`).
  - No explicit access token is passed; same-origin cookie auth is used.
- Authorization inside the hub:
  - `Join(roomName)` enforces membership in the user's `FixedRooms`. Being authenticated is required but not sufficient to join any room.
- Anonymous access
  - Not supported by default. To allow it, you'd remove `[Authorize]` (not recommended for this app) or switch to a JWT bearer model if cross-origin access without cookies is desired.

## Health
Endpoints:
- `/healthz` — basic liveness (string "ok")
- `/healthz/ready` — readiness including Redis/Cosmos and config checks
- `/healthz/metrics` — lightweight in-process metrics snapshot (JSON)

## Development Workflow Tips
* Edit source JS/CSS directly; bundling is optional and not required by default.
* Use `build all` only when you want to produce minified bundles alongside a .NET build.
* Run the `test` task before pushing changes.
* Consider adding a local `--watch` script if iterating frequently (not included by default).
## Future Enhancements (Not Implemented)
* Presence / typing indicators
* Backplane scale-out metrics & multi-instance benchmarks
* Additional anti-abuse policies for OTP attempts (per-user/IP counters in Redis)
* Rich pagination UX (virtualization, skeleton loaders)

## License
See `LICENSE` file.
