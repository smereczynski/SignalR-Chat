# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Project Overview

SignalR Chat is a production-ready real-time chat application built with ASP.NET Core 9, SignalR, Azure Cosmos DB, and Redis. It features fixed chat rooms, dual authentication (OTP + Microsoft Entra ID), read receipts, presence tracking, and comprehensive observability.

**Non-goals**: No direct messages, message editing/deletion, user registration, or file uploads.

---

## Common Commands

### Building and Running

```bash
# Build the solution
dotnet build src/Chat.sln

# Run in-memory mode (no Azure dependencies)
dotnet run --project src/Chat.Web --urls=http://localhost:5099

# Run with Azure resources (requires .env.local)
bash -lc "set -a; source .env.local; dotnet run --project src/Chat.Web --urls=https://localhost:5099"
```

### Testing

```bash
# Run all tests (124 tests)
dotnet test src/Chat.sln

# Run specific test project
dotnet test tests/Chat.Tests/
dotnet test tests/Chat.IntegrationTests/
dotnet test tests/Chat.Web.Tests/

# Run in-memory mode tests
Testing__InMemory=true dotnet test src/Chat.sln

# Run localization tests only
dotnet test tests/Chat.Tests/ --filter "Category=Localization"
```

### Frontend Assets

```bash
# Install dependencies
npm install

# Build production bundles
npm run build:prod
```

---

## High-Level Architecture

### SignalR Hub Pattern (`src/Chat.Web/Hubs/ChatHub.cs`)

**Per-Connection State**: Uses `Context.Items` dictionary to cache user profile and current room, avoiding Redis roundtrips for frequently accessed data.

**Distributed Presence**: Redis-backed `IPresenceTracker` maintains cross-instance user snapshots using JSON-serialized UserViewModel.

**Key Operations**:
- `Join(roomName)`: Validates fixed room membership, updates Redis presence, broadcasts via SignalR groups
- `SendMessage()`: Retrieves context from Items, sanitizes HTML, stores in Cosmos, broadcasts to group
- `MarkRead()`: Rate-limited per user, updates message read-by list, broadcasts status
- `OnConnectedAsync()`: Auto-joins user to default room (preference: explicit default → single fixed room → first alphabetical)
- `OnDisconnectedAsync()`: Tracks connection count per user (multi-tab support), removes presence only when last connection closes

All operations wrapped in OpenTelemetry Activity spans with room/messageId/correlationId tags.

### Repository Pattern (Cosmos DB)

**Three Repositories**: Users, Rooms, Messages, all using shared `CosmosClients` factory.

**CosmosClients Factory** (`src/Chat.Web/Repositories/CosmosRepositories.cs`):
- Manages single CosmosClient + 3 containers with auto-create option
- Gateway mode (not Direct) for private endpoint compatibility
- TTL auto-reconciliation on startup

**Common Patterns**:
- Query-first approach using `GetItemQueryIterator<T>` with parametrized SQL
- `RetryHelper` wraps all operations with exponential backoff (handles 429/503 errors)
- Activity spans for observability tagged with operation name, item counts, HTTP status codes
- Log sanitization on all user-supplied values

**Partition Keys**:
- Users: `userName`
- Rooms: `name`
- Messages: `roomName` (with TTL auto-expiry)

### Authentication Flow

**Three-Part Strategy**: OTP (primary) + Entra ID (SSO) + Silent SSO

**OTP Flow** (`src/Chat.Web/Controllers/AuthController.cs`):
1. `POST /api/auth/start`: Validates user, generates 6-digit code, hashes with Argon2id (pepper + salt + username), stores in Redis (5-min TTL), sends via email/SMS
2. `POST /api/auth/verify`: Checks attempt counter (max 5), compares hash using fixed-time comparison, issues 12-hour sliding cookie on success

**Entra ID Integration**:
- Silent SSO: `SilentSsoMiddleware` issues `prompt=none` challenge on first GET to `/chat` or `/`
- Interactive: `/api/auth/signin/entra` triggers full OAuth flow
- Claim extraction: UPN (preferred_username), tenant ID (tid claim or issuer URL)
- MSA blocking: Denies consumer accounts
- Tenant validation: Enforced if configured with AllowedTenants
- Connection string parsing: Reads `ConnectionStrings:EntraId` format (`ClientId=xxx;ClientSecret=yyy`)

**OTP Storage** (`src/Chat.Web/Services/OtpStore/RedisOtpStore.cs`):
- Key prefix: `otp:username` + `otp_attempts:username`
- Cooldown: 10-second circuit breaker on Redis failures
- Retry wrapper: 3 attempts, 200ms backoff, 1.5s timeout

### Dependency Injection Setup (`src/Chat.Web/Startup.cs`)

**Configuration-Driven Branching**: `Testing:InMemory=true` swaps all services to in-memory implementations.

**Key Registration Patterns**:
- Options pattern: `services.Configure<T>(config.GetSection("Section"))`
- Connection strings: `config.GetConnectionString("Name")` or `config["Name:ConnectionString"]`
- Cosmos client: Singleton lazy instance via `CosmosClients` factory
- Redis multiplexer: Singleton with connection event logging
- Hosted services: Data seeder, unread notification scheduler
- OpenTelemetry: Added after external clients registered

**Rate Limiting**:
```csharp
AddFixedWindowRateLimiter("AuthEndpoints",
    partitionKey: RemoteIpAddress,
    permitLimit: 5,
    window: 60 seconds)
```

### Middleware Pipeline Order (`src/Chat.Web/Startup.cs` Configure method)

1. **GlobalExceptionHandlerMiddleware** - Catches unhandled exceptions, logs with sanitized context
2. **DeveloperExceptionPage** (dev) / **ExceptionHandler** (prod)
3. **HTTPS Redirect** + **HSTS** (prod only, 1-year max-age)
4. **StaticFiles** - wwwroot assets
5. **SecurityHeadersMiddleware** - CSP nonce generation + security headers
6. **RequestLocalization** - Culture provider chain: Cookie → Accept-Language → Default (en)
7. **Routing**
8. **CORS** - SignalRPolicy (origin whitelist in prod, allow-all in dev)
9. **RateLimiter** - Auth endpoints (5 req/min/IP)
10. **SilentSsoMiddleware** - One-time `prompt=none` challenge (if Entra enabled + not authenticated)
11. **RequestTracingMiddleware** - Per-request Activity creation + trace headers
12. **SerilogRequestLogging** - Structured HTTP access logs
13. **Authentication** + **Authorization**
14. **Endpoints** - MapRazorPages, MapControllers, MapHub, MapHealthChecks

### Frontend Architecture (`src/Chat.Web/wwwroot/js/chat.js`)

**Framework**: Vanilla JavaScript (~800 lines), state machine approach

**Core State**:
```javascript
state = {
  profile, rooms, users, messages, joinedRoom,
  pendingMessages: {},      // temp neg ID → message ViewModel
  outbox: [],               // outgoing message queue with retry
  ackTimers: {},            // correlationId → timeout handle
  authStatus,               // UNKNOWN | PROBING | AUTHENTICATED | UNAUTHENTICATED
  isOffline, unreadCount,
  _connectionState: { current, lastUpdate, isReconnecting, reconnectSource }
}
```

**SignalR Connection**:
- Bootstrapped after auth probe completes
- Exponential backoff on failures
- Grace window: 5-10 seconds before showing offline state
- Reconnect telemetry: Sends attempt #, delay, error category to `/api/telemetry/reconnect`

**Message Reconciliation**:
1. Assign temporary negative ID + correlationId on send
2. Hub echoes `newMessage` with server-assigned ID
3. Match by correlationId, fallback to negative ID + content
4. Replace pending entry, cancel ack timer on `messageRead` broadcast

**DOM Rendering**: Minimal, no framework. Functions: `renderRooms()`, `renderUsers()`, `renderMessages()`. Infinite scroll up (paginated loads), auto-scroll-down on new.

### Configuration Pattern (Options)

All configuration uses `IOptions<T>` registered in DI. Binding sources (priority order):
1. `appsettings.{env}.json`
2. Environment variables (e.g., `Cosmos__ConnectionString`)
3. Azure App Service Connection Strings (`GetConnectionString("name")`)
4. PostConfigure callbacks (final override)

**Key Options Classes** (`src/Chat.Web/Options/`):
- **CosmosOptions**: ConnectionString, Database, Container names, MessagesTtlSeconds
- **RedisOptions**: ConnectionString, Database
- **OtpOptions**: Pepper (env override), HashingEnabled, MaxAttempts, Argon2 params
- **EntraIdOptions**: Instance, TenantId, ClientId, ClientSecret, CallbackPath, IsEnabled, Authorization, Fallback, AutomaticSso
- **AcsOptions**: ConnectionString, EmailFrom, SmsFrom
- **CorsOptions**: AllowAllOrigins, AllowedOrigins
- **RateLimitingOptions**: Auth, MarkRead
- **NotificationOptions**: Enabled, DelaySeconds, AttemptLimit

**Azure App Service on Linux**: Use double underscore (`__`) in environment variables for hierarchy: `Cosmos__Database` (not `Cosmos:Database`).

---

## Key Architectural Patterns

### Resilience

**RetryHelper** (`src/Chat.Web/Utilities/RetryHelper.cs`): Exponential backoff with configurable max attempts, per-attempt timeout, transient detection predicate.

**Transient Classifiers** (`src/Chat.Web/Utilities/Transient.cs`):
- `IsCosmosTransient`: 429, 503, timeouts
- `IsRedisTransient`: connection failures, timeouts

**Circuit Breaker**: Redis OTP store uses 10-second cooldown on consecutive failures, returns safe defaults.

### Observability (OpenTelemetry + Serilog)

**Traces**: Activity per HTTP request + per Cosmos/Redis operation, tagged with semantic attributes (`chat.room`, `app.messageId`, `db.status_code`).

**Metrics**: Custom counters (`messages.sent`, `otp.requests`, `otp.verifications`, `rooms.joined`), runtime metrics (CPU, memory, GC).

**Logs**: Structured via Serilog, enriched with trace ID, request context, sanitized user inputs.

**Exporters**: Auto-selection (Azure Monitor → OTLP → Console) based on connection string availability.

### Security

**Log Sanitization** (`src/Chat.Web/Utilities/LogSanitizer.cs`): CRITICAL - Always use `LogSanitizer.Sanitize()` before logging any user-supplied input (HTTP headers, form data, query params, request body). Strips newlines, control chars, masks emails/phones. Prevents log injection attacks (CWE-117).

**Bad** (vulnerable):
```csharp
_logger.LogWarning("Invalid origin: {Origin}", context.Request.Headers["Origin"]);
```

**Good** (sanitized):
```csharp
var origin = context.Request.Headers["Origin"].ToString();
_logger.LogWarning("Invalid origin: {Origin}", LogSanitizer.Sanitize(origin));
```

**Other Security**:
- Fixed-time comparison for OTP verification
- CSP with per-request nonce for inline scripts
- Cookie security: HttpOnly, Secure (HTTPS), SameSite=Lax, 12-hour sliding expiry
- CORS: Whitelist-enforced in production
- Rate limiting: IP-based for OTP endpoints, per-user for MarkRead

### Testing Support

Set `Testing__InMemory=true` to swap all external services for in-memory implementations. Integration tests can run against live app with `TestAuthHandler` for fixed identity.

---

## Code Style

### C# (4 spaces)
- Follow [Microsoft C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- PascalCase for classes/methods, camelCase for local variables/parameters
- Nullable reference types: `string?` for nullable
- Prefer `async`/`await` for I/O
- Use `ILogger<T>` for logging, never `Console.WriteLine` in production
- Dependency injection for all services

### JavaScript (2 spaces)
- Modern ES6+ syntax (const/let, arrow functions, template literals)
- NO jQuery - use vanilla JavaScript
- camelCase for variables/functions, PascalCase for classes
- `async`/`await` for asynchronous operations

### Bicep (2 spaces)
- Always add `@description()` annotations
- Use `@secure()` for secrets
- Use Linux for App Service
- Use double underscore (`__`) notation for hierarchical app settings (not colon `:`)

### Testing
- xUnit framework
- Test names: `MethodName_Scenario_ExpectedBehavior`
- Aim for >80% coverage on new code

---

## Quick Reference: Adding New Features

### Add Repository Operation
1. Define interface method in `I{Entity}Repository`
2. Add Cosmos query in `Cosmos{Entity}Repository` with Activity span, retry wrapper, status tagging
3. Add in-memory fallback in `InMemory{Entity}Repository`
4. Inject in hub/controller via DI

### Add SignalR Method
1. Define async method in `ChatHub`, tagged `[Authorize]`
2. Get user/room from `Context.Items` (per-connection state)
3. Persist via repository, broadcast via `Clients.Group(roomName).SendAsync()`
4. Wrap in Activity span, log failures, emit metrics

### Add Configuration Option
1. Create `OptionsClassName` in `src/Chat.Web/Options/` with public properties
2. Register in `Startup.ConfigureServices`: `services.Configure<OptionsClassName>(config.GetSection("SectionName"))`
3. Inject via `IOptions<OptionsClassName>` in services/controllers
4. Read from `appsettings.json` or environment variable (`SectionName__PropertyName=value`)

---

## Important Files

- **Hubs**: `src/Chat.Web/Hubs/ChatHub.cs` - Real-time messaging
- **Repositories**: `src/Chat.Web/Repositories/` - Data access layer
- **Authentication**: `src/Chat.Web/Controllers/AuthController.cs` - OTP flow
- **Startup**: `src/Chat.Web/Startup.cs` - DI registration, middleware pipeline
- **Frontend**: `src/Chat.Web/wwwroot/js/chat.js` - SignalR client
- **Utilities**: `src/Chat.Web/Utilities/LogSanitizer.cs` - CRITICAL for security
- **Options**: `src/Chat.Web/Options/` - Configuration classes

---

## Git Workflow

Follow [Conventional Commits](https://www.conventionalcommits.org/):
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `test`: Adding or updating tests
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `chore`: Maintenance tasks

**Branch Naming**: `feature/your-feature-name` or `fix/bug-description`

**NEVER merge branches locally** - always create a Pull Request on GitHub for review.

Ensure all tests pass before committing.

---

## Fixed Users

Users are pre-populated (no registration):
- alice, bob, charlie, dave, eve

OTP codes printed to console in in-memory mode (6-digit codes).

---

## Health Checks

- `/healthz` - Liveness probe (responds 200 OK)
- `/healthz/ready` - Readiness probe (checks Cosmos + Redis)

---

## Localization

9 languages supported: English (en), Polish (pl), Spanish (es), French (fr), German (de), Italian (it), Portuguese (pt), Japanese (ja), Chinese (zh)

Server-side: `.resx` files in `src/Chat.Web/Resources/`
Client-side: JSON files in `src/Chat.Web/wwwroot/locales/`

After changing `.resx` files: `dotnet build src/Chat.sln`

## Tools

There are multiple MCP server available especialy in docker to be used when analyzing the code, working with code and fixing the code. Use them extensively.