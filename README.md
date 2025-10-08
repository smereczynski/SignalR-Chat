# SignalR-Chat
Real-time multi-room chat on .NET 9 using SignalR (in‑process hub), EF Core persistence, Redis for OTP codes (or in‑memory when testing), optional Azure SignalR (configured automatically when not in test mode), OpenTelemetry (traces + metrics + logs) and a small vanilla JavaScript client (bundled/minified).

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
2. Code persisted with TTL in configured OTP store
3. User submits code; on success a cookie auth session is issued
4. Client starts (or reuses) hub connection; queued optimistic messages (if any) flush

Console output displays the OTP when ACS is not configured.

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
* OTP codes stored plaintext in the chosen store (hashing can be added for stronger threat model)
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
* Hashed OTP storage + additional anti-abuse policies
* Rich pagination UX (virtualization, skeleton loaders)

## License
See `LICENSE` file.
