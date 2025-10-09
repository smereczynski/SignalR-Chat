# SignalR-Chat
Real-time multi-room chat on .NET 9 using SignalR (in‑process hub), EF Core persistence, Redis for OTP codes (or in‑memory when testing), optional Azure SignalR (configured automatically when not in test mode), OpenTelemetry (traces + metrics + logs) and a small vanilla JavaScript client (bundled/minified). OTP codes are stored hashed by default using Argon2id with a per-code salt and an environment-supplied pepper.

The project intentionally keeps scope tight: fixed public rooms, text messages only, no editing/deleting, and OTP-based demo authentication.

## Implemented Features (Current State)
* Multi-room chat (fixed rooms: `general`, `ops`, `random`)
* Text messages only (immutable after send)
* Optimistic send with client correlation IDs and reconciliation
* Incremental pagination (newest batch first; fetch older on upward scroll)
* Client-side send pacing (basic rate limiting logic in JS)
* Avatar initials (derived client-side with cache bust/refresh protection)
* OTP authentication (cookie session)
  * Demo users: `alice`, `bob`, `charlie`
  * OTP code stored in Redis (or in-memory store under test flag)
  * Hashed storage by default (Argon2id + salt + pepper) with a versioned format
  * Console fallback delivery (ACS email/SMS supported only if configured)
* Connection & reconnect telemetry (duplicate start suppression + backoff attempts counter)
* OpenTelemetry traces + metrics + logs; custom counters for chat domain events
* Health endpoint: `/healthz` (readiness/basic liveness)
* Outbox queue: pending messages buffered while disconnected and flushed after reconnect & room join
* Duplicate hub start guard (prevents false reconnect storms)
* SessionStorage backed optimistic message reconciliation

## Fixed Room Topology
Rooms are static; there is no runtime CRUD. The seeding hosted service ensures the three canonical rooms and demo users exist on startup.

## Architecture Overview
**Runtime**: ASP.NET Core 9 (Razor Pages + Controllers + SignalR Hub)  
**Real-time**: SignalR hub (Azure SignalR automatically added when not running in in-memory test mode)  
**Persistence**: EF Core context for users, rooms, messages (Cosmos repositories or in-memory depending on config)  
**OTP / Cache**: Redis (or in-memory fallback) storing short-lived OTP codes (`otp:{user}`)  
**Auth Flow**: Request code → store in OTP store → user enters code → cookie issued → hub connects  
**Observability**: OpenTelemetry (trace + metric + log providers) with exporter priority (Azure Monitor > OTLP > Console) and domain counters  
**Frontend**: Source JS in `wwwroot/js/` compiled to minified assets in `wwwroot/js/dist/` (pages reference only the dist versions)

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
The workspace includes curated tasks (see `.vscode/tasks.json`). Key tasks:

| Task Label | Purpose |
|------------|---------|
| npm install | Install frontend dependencies (esbuild, sass). |
| bundle js (prod) | Build/minify JS + compile Sass (depends on npm install). |
| dotnet build | Compile the .NET solution. |
| build all | Full pipeline: npm install → bundle js (prod) → dotnet build. |
| test | Run solution tests (`dotnet test --no-build`). |
| Run Chat (Azure local env) | Load `.env.local` (if present), set Development environment, run app on http://localhost:5099. |
| PROD Run Chat (Azure local env) | Same as above but forces `ASPNETCORE_ENVIRONMENT=Production`. |

Recommended editing cycle:
1. Modify source JS (`wwwroot/js/chat.js` or `site.js`)
2. Run task: `bundle js (prod)` (or include in `build all`)
3. Run `Run Chat (Azure local env)` task
4. Refresh browser

Never edit files under `wwwroot/js/dist/` directly—changes will be overwritten by the bundling step.

## OTP Authentication (Summary)
1. User selects demo identity and requests code
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

## Security Notes
* OTP codes are stored hashed by default. To support legacy/testing scenarios, plaintext storage can be toggled with `Otp:HashingEnabled=false`.
* Provide a high-entropy Base64 pepper via `Otp__Pepper` in each environment. Keep this secret out of source control.
* Rate limiting applied to auth/OTP endpoints via fixed window limiter (configurable limits)
* Correlation IDs are random UUIDs (no sensitive data embedded)

## Health
`/healthz` basic readiness (string "ok"). No additional JSON metrics endpoint is currently exposed.

## Development Workflow Tips
* Edit only source JS/CSS; let tasks produce minified output
* Use `build all` for a clean full pipeline before commits
* Run `test` task before pushing changes
* Consider adding a local `--watch` script if iterating frequently (not included by default)
## Future Enhancements (Not Implemented)
* Presence / typing indicators
* Backplane scale-out metrics & multi-instance benchmarks
* Additional anti-abuse policies for OTP attempts (per-user/IP counters in Redis)
* Rich pagination UX (virtualization, skeleton loaders)

## License
See `LICENSE` file.
