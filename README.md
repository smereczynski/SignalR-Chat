# SignalR-Chat

**Version**: 0.9.3

Real-time multi-room chat on .NET 9 using SignalR (in‑process hub), Azure Cosmos DB persistence, Redis for OTP codes (or in‑memory when testing), optional Azure SignalR (configured automatically when not in test mode), OpenTelemetry (traces + metrics + logs) and a small vanilla JavaScript client. OTP codes are stored hashed by default using Argon2id with a per-code salt and an environment-supplied pepper.

The project intentionally keeps scope tight: fixed public rooms, text messages only, no editing/deleting, and OTP-based authentication.

## Implemented Features (Current State)
* Multi-room chat (fixed rooms: `general`, `ops`, `random`)
* Text messages only (immutable after send)
* Multi-language support: 9 locales with natural translations (en, pl-PL, de-DE, cs-CZ, sk-SK, uk-UA, be-BY, lt-LT, ru-RU)
  * Culture resolution via cookie preference or Accept-Language header
  * Client-side translations via REST API endpoint
  * Culture switcher UI on login page
* Optimistic send with client correlation IDs and reconciliation
* Single ack-timeout per message (deduped by correlationId to avoid duplicate retries)
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
* Read receipts: message ReadBy is persisted and broadcast so clients can show who has read each message
* Delayed unread notifications: if a message remains unread after a configurable delay, send notification via email/SMS

## Fixed Room Topology
Rooms are static; there is no runtime CRUD. Rooms and initial users must be provisioned via the bootstrap script (see `docs/BOOTSTRAP.md` for details).

## Architecture Overview
**Runtime**: ASP.NET Core 9 (Razor Pages + Controllers + SignalR Hub)  
**Real-time**: SignalR hub (Azure SignalR automatically added when not running in in-memory test mode)  
**Persistence**: Azure Cosmos DB with custom repository pattern (or in-memory repositories for testing)  
**OTP / Cache**: Redis (or in-memory fallback) storing short-lived OTP codes (`otp:{user}`)  
**Auth Flow**: Request code → store in OTP store → user enters code → cookie issued → hub connects  
**Localization**: ASP.NET Core Localization with 9 supported markets; culture via Cookie > Accept-Language; API endpoint for client translations  
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

## Configuration
The app uses standard ASP.NET Core configuration with environment-specific overrides:

* **`appsettings.Development.json`**: Development-time settings including relaxed CORS, in-memory testing flags, console telemetry, and development rate limits.
* **`appsettings.Production.json`**: Production-ready settings with stricter rate limits, Azure Monitor telemetry integration, and production-grade security policies.

Key configuration sections:
* `Otp`: OTP hashing parameters (Argon2id memory, iterations, parallelism), TTL, hashing toggle
* `RateLimiting`: Per-endpoint rate limits (OTP request/verify, MarkRead operations)
* `Cosmos`: Connection strings, database/container names, messages TTL
* `Redis`: Connection string for OTP storage and caching
* `AzureSignalR`: Connection string (auto-added when not in test mode)
* `Acs`: Azure Communication Services for email/SMS delivery
* `Notifications`: Unread message notification delays and channels
* `OTel`: OpenTelemetry exporter endpoints (Azure Monitor, OTLP)

Environment variables override appsettings values using the standard colon-to-double-underscore mapping (e.g., `Otp__Pepper`, `Cosmos__MessagesTtlSeconds`).

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

### Read receipts
- Clients mark messages as read when they reach the bottom of the timeline or when messages come into view, debounced to reduce chatter.
- Server persists the reader in each message's `ReadBy` set and broadcasts `messageRead` events for real-time UI updates.
- REST fallback: `POST /api/messages/{id}/read` marks a message as read for the current user.

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

## Notifications (Unread messages)
When a message remains unread after a configurable delay, the app sends a lightweight notification to room members (excluding the sender and anyone who has already read it).

- Delay: `Notifications:UnreadDelaySeconds` (default 60). After this delay, unread messages trigger notifications.
- Recipient selection: primarily from `room.users`; when that list is missing, a fallback infers recipients from users' `fixedRooms`.
- Channels: Email and SMS via Azure Communication Services when configured; otherwise a console fallback is used in development.
- Formatting (required contract):
  - Email subject: `New message`
  - Email body: `New message in #<room>`
  - SMS body: `New message in #<room>`
  - No message content is included in notifications.

Implementation details:
- `UnreadNotificationScheduler` is an `IHostedService` that schedules a per-message delayed check and sends notifications if still unread.
- `NotificationSender` constructs the notification payload; `AcsOtpSender` applies the email subject for notifications while preserving OTP-specific formatting for the authentication flow.

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
## Presence Tracking
The application tracks user presence across multiple instances using a hybrid approach:
* **Per-connection state**: Stored in `Context.Items` for instant access in hub methods (no Redis query needed for SendMessage/MarkRead)
* **Distributed snapshot**: Redis hash (`presence:users`) stores user presence for cross-instance consistency
* **Interfaces**: `IPresenceTracker` with `RedisPresenceTracker` (production) and `InMemoryPresenceTracker` (testing)
* **Endpoints**: `GET /api/health/chat/presence` (authenticated) returns per-room user presence

## Localization
The application supports **9 markets** with natural, idiomatic translations:

### Supported Cultures
| Culture | Language | Notes |
|---------|----------|-------|
| `en` | English | Default/fallback |
| `pl-PL` | Polish | |
| `de-DE` | German | Formal style |
| `cs-CZ` | Czech | Informal friendly |
| `sk-SK` | Slovak | |
| `uk-UA` | Ukrainian | Cyrillic, modern Ukrainian |
| `be-BY` | Belarusian | Cyrillic, proper Belarusian |
| `lt-LT` | Lithuanian | Baltic language, Latin script |
| `ru-RU` | Russian | Cyrillic, standard contemporary |

### Features
* **Culture Resolution**: Cookie preference → Accept-Language header → Default (en)
* **Client API**: `GET /api/localization/strings` returns JSON with 60+ translated strings
* **Resource Files**: `SharedResources.[locale].resx` with comprehensive translations
* **Culture Switcher**: UI component on login page for explicit culture selection
* **Coverage**: Application UI, chat interface, authentication, errors, validation, notifications
* **Translation Quality**: Natural, idiomatic translations (not literal machine translations)

### Client Integration
JavaScript code fetches translations on page load and populates `window.i18n` object:
```javascript
const response = await fetch('/api/localization/strings');
window.i18n = await response.json();
// Usage: window.i18n.Loading, window.i18n.Error, etc.
```

Razor pages use `IStringLocalizer<SharedResources>`:
```csharp
@inject IStringLocalizer<SharedResources> Localizer
@Localizer["AppTitle"]
```

### Testing
Comprehensive test coverage with 55 tests across all 9 locales:
* Basic English localization tests (10 tests)
* Multi-locale tests (45 tests = 5 test categories × 9 locales)
  * App translations verification
  * UI strings validation
  * Authentication strings checking
  * Complete key existence validation (60+ keys per locale)
  * Parameterized string formatting

All 55 tests passing ✅

## Testing
The project includes comprehensive test coverage across three test assemblies:

### Test Summary
* **Total Tests**: 109 tests
* **Status**: All passing ✅
* **Test Projects**:
  - `Chat.Tests` (80 tests): Unit tests including localization (55 tests), OTP hashing, configuration guards, unread notifications
  - `Chat.DataSeed.Tests` (10 tests): Data seeding validation
  - `Chat.IntegrationTests` (19 tests): End-to-end integration tests including OTP flow, rate limiting, room authorization, hub lifecycle

### Key Test Categories
1. **Localization Tests** (55 tests in Chat.Tests)
   - English default culture tests (10)
   - Multi-locale comprehensive tests (45 = 5 categories × 9 locales)
   - Covers all 60+ resource keys across 9 cultures
   - Validates translation quality and completeness

2. **Integration Tests** (19 tests in Chat.IntegrationTests)
   - `OtpAuthFlowTests`: Full OTP authentication workflow
   - `RoomAuthorizationTests`: Room access control validation
   - `RoomJoinPositiveTests`: Successful room join scenarios
   - `ChatHubLifecycleTests`: SignalR hub connection/disconnection
   - `RateLimitingTests`: OTP endpoint rate limiting
   - `MarkReadRateLimitingTests`: Read receipt rate limiting
   - `ImmediatePostAfterLoginTests`: REST fallback (feature-flagged)

3. **Unit Tests** (25 tests in Chat.Tests, excluding localization)
   - `OtpHasherTests`: Argon2id hashing, salt, pepper verification
   - `ConfigurationGuardsTests`: Required configuration validation
   - `UnreadNotificationSchedulerTests`: Delayed notification logic
   - `UrlIsLocalUrlTests`: Redirect validation

4. **Data Tests** (10 tests in Chat.DataSeed.Tests)
   - Bootstrap data seeding validation
   - User/Room/Message seed data integrity

### Running Tests
```bash
# Run all tests
dotnet test src/Chat.sln --nologo

# Run specific test project
dotnet test tests/Chat.Tests/Chat.Tests.csproj --nologo

# Run localization tests only
dotnet test src/Chat.sln --filter "FullyQualifiedName~LocalizationTests" --nologo

# Run integration tests only
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --nologo
```

### VS Code Task
Use the `test` task defined in `.vscode/tasks.json`:
```bash
# Via VS Code: Tasks: Run Task → test
# Or use the task runner
```

### Test Configuration
- Integration tests use `CustomWebApplicationFactory` with in-memory repositories
- `Testing:InMemory=true` flag enables in-memory OTP store and repositories
- No external dependencies required (Redis/Cosmos mocked)
- Tests run in isolation with separate DI containers

## Future Enhancements (Not Implemented)
* Typing indicators
* Backplane scale-out metrics & multi-instance benchmarks
* Additional anti-abuse policies for OTP attempts (per-user/IP counters in Redis)
* Rich pagination UX (virtualization, skeleton loaders)

## License
See `LICENSE` file.
