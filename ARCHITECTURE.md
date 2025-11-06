# Architecture

This document describes the high-level architecture of SignalR-Chat with focus on infrastructure provisioning, OTP hashing implementation, and runtime components.

## Table of Contents

- [Infrastructure as Code (Bicep)](#infrastructure-as-code-bicep)
- [Diagrams](#diagrams)
- [Runtime Overview](#runtime-overview)
- [OTP Authentication and Hashing](#otp-authentication-and-hashing)
- [Observability](#observability)
- [Cosmos Messages Retention (TTL)](#cosmos-messages-retention-ttl)
- [Localization](#localization)
- [Current Overview](#current-overview)
- [Key Components](#key-components)
- [SignalR Role](#signalr-role)
- [Redis Role](#redis-role)
- [Runtime Flows](#runtime-flows)
- [Build & Front-End Delivery](#build--front-end-delivery)
- [Observability & Telemetry](#observability--telemetry)
- [Scaling Considerations](#scaling-considerations)
- [Security Notes](#security-notes)
- [Future Roadmap (Prioritized)](#future-roadmap-prioritized)
- [Glossary](#glossary)
- [Data Schemas](#data-schemas)

---

## Infrastructure as Code (Bicep)

The application uses **Azure Bicep** templates for reproducible infrastructure deployments across dev, staging, and production environments. All deployments are automated through **GitHub Actions** with environment-specific configurations.

### Infrastructure Components

| Component | Purpose | Configuration |
|-----------|---------|---------------|
| **Virtual Network** | Network isolation with TWO dedicated subnets | /26 CIDR per environment |
| **App Service Subnet** | Delegated to `Microsoft.Web/serverFarms` for VNet integration | /27 CIDR (first subnet) |
| **Private Endpoints Subnet** | Secure Azure service connections with private endpoints | /27 CIDR (second subnet) |
| **Network Security Groups** | Security rules for both subnets | HTTPS (443), HTTP (80), internal traffic |
| **Log Analytics Workspace** | Centralized logging and monitoring | 30/90/365 day retention |
| **Application Insights** | Application performance monitoring | Workspace-based (not classic) |
| **Cosmos DB** | NoSQL database with 3 containers | Zone-redundant for all environments, geo-replication for prod |
| **Azure Managed Redis** | OTP storage, session cache, presence tracking | Balanced_B1 (dev), Balanced_B3 (staging/prod) |
| **Azure SignalR Service** | Real-time communication hub | Standard_S1 for all environments (1/1/5 units) |
| **Azure Communication Services** | Email and SMS capabilities | Global resource, Europe data location |
| **App Service Plan** | Web application hosting | P0V4 PremiumV4 for all environments |
| **App Service (Web App)** | SignalR Chat application | VNet integrated, HTTPS-only, TLS 1.2, identity disabled |
| **Private Endpoints** | Secure connections to Cosmos DB, Redis, SignalR | Deployed in private endpoints subnet |

### Network Architecture (Critical Requirement)

Each environment **must** have a Virtual Network with **exactly TWO subnets**:

1. **App Service Subnet** (First subnet):
   - Purpose: VNet integration for App Service
   - Delegation: `Microsoft.Web/serverFarms`
   - NSG Rules: Allow HTTPS (443), HTTP (80) inbound
   - Connected to: App Service via VNet integration
   - Naming: IP-based format (e.g., `10-0-0-0--27` for 10.0.0.0/27)

2. **Private Endpoints Subnet** (Second subnet):
   - Purpose: Private Endpoint connections to Cosmos DB, Redis, SignalR
   - NSG Rules: Restrictive rules for internal traffic only
   - Private Endpoints: Cosmos DB (Sql), Redis (redisEnterprise), SignalR (signalr)
   - Naming: IP-based format (e.g., `10-0-0-32--27` for 10.0.0.32/27)

### Deployment Strategy

**GitHub Actions Only** (`.github/workflows/deploy-infrastructure.yml`):
- All deployments are triggered via GitHub Actions workflow
- Environment selection: dev, staging, prod (manual dispatch)
- Approval gates required for production deployments
- Automatic what-if preview before deployment
- Post-deployment validation (2 subnets check)
- Database seeding after successful deployment
- Optional teardown action for cleanup

**Environment Variables** (GitHub repository secrets/variables):
All 6 parameters are configured per environment as GitHub variables:
- `BICEP_BASE_NAME`: Base name for resources (e.g., `signalrchat`)
- `BICEP_LOCATION`: Azure region (e.g., `polandcentral`)
- `BICEP_VNET_ADDRESS_PREFIX`: VNet CIDR (e.g., `10.0.0.0/26`)
- `BICEP_APP_SERVICE_SUBNET_PREFIX`: First subnet CIDR (e.g., `10.0.0.0/27`)
- `BICEP_PRIVATE_ENDPOINTS_SUBNET_PREFIX`: Second subnet CIDR (e.g., `10.0.0.32/27`)
- `BICEP_ACS_DATA_LOCATION`: ACS data location (e.g., `Europe`)

**Resource Naming Convention** (from issue #84):
```
Resource Group:     rg-{codename}-{env}-{location}
App Service Plan:   serverfarm-{codename}-{env}-{location}
App Service:        {codename}-{env}-{location}
Cosmos DB:          cdb-{codename}-{env}-{location}
Redis:              redis-{codename}-{env}-{location}
SignalR:            sigr-{codename}-{env}-{location}
ACS:                acs-{codename}-{env}
App Insights:       ai-{codename}-{env}-{location}
Log Analytics:      law-{codename}-{env}-{location}
Virtual Network:    vnet-{codename}-{env}-{location}
Private Endpoint:   pe-{resourcename}
PE Network Interface: nic-pe-{resourcename}
```

Examples:
- Virtual Network: `vnet-signalrchat-dev-polandcentral`
- App Service: `signalrchat-prod-polandcentral`
- Cosmos DB: `cdb-signalrchat-staging-polandcentral`
- Private Endpoint: `pe-redis-signalrchat-dev-polandcentral`

### Environment SKU Matrix

| Resource | Dev | Staging | Production |
|----------|-----|---------|------------|
| App Service Plan | P0V4 PremiumV4 (1 instance, no AZ) | P0V4 PremiumV4 (2 instances, AZ) | P0V4 PremiumV4 (3 instances, AZ) |
| Cosmos DB | Zone-redundant, single region | Zone-redundant, single region | Zone-redundant, multi-region (polandcentral + germanywestcentral) |
| Redis Cache | Balanced_B1 (Azure Managed Redis) | Balanced_B3 (Azure Managed Redis) | Balanced_B5 (Azure Managed Redis) |
| SignalR Service | Standard_S1 (1 unit) | Standard_S1 (1 unit) | Standard_S1 (5 units) |
| Log Analytics | 30-day retention, 1 GB cap | 90-day retention, 5 GB cap | 365-day retention, 10 GB cap |
| Private Endpoints | Enabled for all services | Enabled for all services | Enabled for all services |

### Cost Estimates
- **Development**: ~$150-250/month (P0V4 + Standard SignalR + Managed Redis)
- **Staging**: ~$350-500/month (2 instances + zone redundancy)
- **Production**: ~$1200-1800/month (3 instances + multi-region + 5 SignalR units)

### Deployment Documentation

For detailed deployment instructions, see:
- **[Infrastructure README](infra/bicep/README.md)**: Complete Bicep documentation with architecture diagrams, deployment steps, and troubleshooting
- **[Bootstrap Guide](docs/BOOTSTRAP.md)**: Step-by-step guide for infrastructure provisioning and data seeding

---

## Diagrams

The following Mermaid diagrams visualize the system components and key runtime flows.

### Infrastructure Architecture (Azure)

```mermaid
flowchart TB
  subgraph Internet
    U[Users/Browsers]
  end
  
  subgraph Azure["Azure Subscription"]
    subgraph RG["Resource Group (rg-signalrchat-{env})"]
      subgraph VNet["Virtual Network (10.X.0.0/16)"]
        subgraph AppSubnet["App Service Subnet (10.X.1.0/24)<br/>Delegated: Microsoft.Web/serverFarms"]
          AS["App Service<br/>(Chat.Web)<br/>VNet Integrated"]
        end
        
        subgraph PESubnet["Private Endpoints Subnet (10.X.2.0/24)"]
          PE[Private Endpoints<br/>(Future)]
        end
      end
      
      NSG1["NSG: appservice-subnet"]
      NSG2["NSG: privateendpoints-subnet"]
      
      ASignalR["Azure SignalR Service<br/>(Free F1/Standard S1)"]
      Redis["Azure Cache for Redis<br/>(Basic/Standard/Premium)"]
      Cosmos["Cosmos DB (NoSQL)<br/>(Serverless/Standard)<br/>messages | users | rooms"]
      ACS["Azure Communication Services<br/>(Email/SMS)"]
      
      LAW["Log Analytics Workspace<br/>(30/90/365d retention)"]
      AI["Application Insights<br/>(Workspace-based)"]
    end
  end
  
  U -->|HTTPS| AS
  AS -.->|VNet Integration| AppSubnet
  AS -->|Real-time| ASignalR
  AS -->|OTP, Cache, Presence| Redis
  AS -->|Data| Cosmos
  AS -->|Notifications| ACS
  AS -->|Telemetry| AI
  AI -->|Logs/Metrics| LAW
  
  NSG1 -.->|Secures| AppSubnet
  NSG2 -.->|Secures| PESubnet
  
  style VNet fill:#e1f5ff
  style AppSubnet fill:#ffe1e1
  style PESubnet fill:#fff4e1
  style RG fill:#f0f0f0
```

Notes:
- Each environment (dev/staging/prod) has its own Resource Group with isolated VNet
- VNet has **exactly TWO subnets** (critical requirement for issue #84)
- App Service is VNet integrated via delegated subnet
- Private Endpoints subnet reserved for future secure connections
- All resources follow naming convention: `{baseName}-{env}-{type}[-{suffix}]`

### System Architecture (Runtime)

```mermaid
flowchart LR
  subgraph Client
    B[Browser]
  end
  subgraph App[Chat.Web App]
    W[ASP.NET Core + SignalR]
  end
  subgraph Azure[Azure Services]
    AS[Azure SignalR Service]
    R[(Redis OTP store)]
    C[(Cosmos DB Users/Rooms/Messages)]
    ACS[Azure Communication Services]
  end
  subgraph Observability
    OTLP[OTLP endpoint Tempo/Loki/Prometheus]
    AM[Azure Monitor Exporter]
  end

  B --> W
  W --- AS
  W --- R
  W --- C
  W --- ACS
  W --- OTLP
  W --- AM
```

Notes:
- Azure SignalR is used in non-test modes; in Testing:InMemory the app uses in-process SignalR only.
- Redis is used for OTP storage, rate limiting counters, and presence tracking; messages/users/rooms are stored in Cosmos DB.
- Observability exporters are selected by configuration: Azure Monitor > OTLP > Console.

### OTP authentication flow

```mermaid
sequenceDiagram
  autonumber
  actor U as User
  participant B as Browser
  participant A as Chat.Web
  participant R as Redis (OTP)
  participant C as Azure Communication Services

  U->>B: Initiate login
  B->>A: POST /api/auth/start { user }
  A->>R: SET otp:{user} = OtpHash(...), TTL
  A-->>C: Send OTP (Email/SMS) [if configured]
  A-->>B: 200 OK { status: "sent" }

  U->>B: Enter OTP code
  B->>A: POST /api/auth/verify { user, code }
  A->>R: GET otp:{user}
  alt Stored is OtpHash:*
    A-->>A: Verify Argon2id (pepper+salt)
  else Legacy plaintext
    A-->>A: FixedTimeEquals
  end
  alt Valid
    A-->>B: 200 OK + set-cookie (auth)
    B->>A: GET /chat
    A-->>B: Razor Page + scripts
    B-->>A: SignalR negotiate/start
    A-->>B: Connection established
  else Invalid
    A-->>B: 400/401 Invalid OTP
  end
```

### Messaging, optimistic UI, and reconnect

```mermaid
sequenceDiagram
  autonumber
  participant B as Browser (chat.js)
  participant H as ChatHub (server)
  participant D as Cosmos DB

  Note over B: User types message (allocate correlationId)
  B-->>B: Optimistic render in UI
  B->>H: hub.sendMessage({ text, correlationId })
  H->>D: Persist message
  H-->>B: Broadcast canonical message { correlationId }
  B-->>B: Reconcile optimistic placeholder

  rect rgba(33,66,99,0.06)
  Note over B,H: Background tab or network drop
  break Disconnected
    B-->>B: Enqueue messages in outbox (sessionStorage)
  end
  par Reconnect triggers
    B-->>B: visibilitychange (becomes visible)
  and Online event
    B-->>B: window.online
  end
  B->>H: ensureConnected() with infinite reconnect
  H-->>B: Reconnected + rejoined room
  B-->>B: Flush outbox FIFO
  end
```

### Read receipts flow

```mermaid
sequenceDiagram
  autonumber
  participant B as Browser (chat.js)
  participant H as ChatHub
  participant D as Store (Cosmos/EF)

  Note over B: User views message list (render/scroll/focus/visibility)
  B-->>B: Collect visible, non-mine, unread message ids
  B-->>B: Debounce 300ms + de-dup cache (prevent repeats)
  loop For each id in visibleIds
    B->>H: markRead(id)
    H->>D: Add reader to Message.ReadBy
    H-->>B: Broadcast messageRead { id, readers }
  end
  B-->>B: Update UI badges (per-room and per-message)
```

## Runtime overview
- ASP.NET Core 9 (Razor Pages + Controllers + SignalR Hub)
- Persistence: Azure Cosmos DB with custom repository pattern; in-memory repositories when Testing:InMemory mode enabled
- OTP store: Redis in normal mode, in-memory in Testing:InMemory
- Auth: Cookie authentication after OTP verification (dedicated `/login` page)
- Localization: ASP.NET Core Localization with 9 supported markets (en, pl-PL, de-DE, cs-CZ, sk-SK, uk-UA, be-BY, lt-LT, ru-RU); culture resolution via Cookie > Accept-Language header; API endpoint (`/api/localization/strings`) provides client-side translations
- Observability: OpenTelemetry (traces, metrics, logs) with exporter auto-selection
- SignalR transport: In-process by default; Azure SignalR is automatically added when not in Testing:InMemory mode
- Read receipts: Client marks messages as read in two ways: (1) on reaching timeline bottom (existing), and (2) after join/reconnect by scanning the viewport for visible, non-self messages and invoking `markRead` per id (debounced, idempotent). This ensures historical messages seen on reconnect are recorded as read.

## OTP authentication and hashing

### Goals
- Avoid storing OTP codes in plaintext while preserving a simple UX.
- Support a migration path from legacy plaintext values without breaking existing sessions.
- Keep implementation configurable and versioned for future upgrades.

### Components
- `IOtpHasher` (src/Chat.Web/Services/IOtpHasher.cs)
  - Contract: `string Hash(string userName, string code)` and `VerificationResult Verify(string userName, string code, string stored)`
  - `VerificationResult` has `IsMatch` and `NeedsRehash` flags.
- `Argon2OtpHasher` (src/Chat.Web/Services/Argon2OtpHasher.cs)
  - Uses Isopoh.Cryptography.Argon2 (managed .NET implementation)
  - Algorithm: Argon2id (HybridAddressing in Isopoh enum)
  - Per-code random 16-byte salt
  - Preimage: `pepper || userName || ':' || salt || ':' || code`
  - Stores a versioned record with explicit KDF parameters:
    - Format: `OtpHash:v2:argon2id:m={KB},t={it},p={par}:{saltB64}:{encoded}`
    - `encoded` is the library’s PHC-style encoded string for the computed hash
  - Verification: recomputes the same preimage using the stored salt and calls `Argon2.Verify(encoded, preimage, threads)`
  - `NeedsRehash` is true when configured parameters are stronger than those embedded in the stored record
- `OtpOptions` (src/Chat.Web/Options/OtpOptions.cs)
  - `Pepper` (Base64 string) – load from environment variable `Otp__Pepper`
  - `HashingEnabled` (default true)
  - Argon2 parameters: `MemoryKB`, `Iterations`, `Parallelism`, `OutputLength`

### DI and configuration
- Registered in `Startup.ConfigureServices`:
  - `services.Configure<OtpOptions>(Configuration.GetSection("Otp"));`
  - `services.PostConfigure<OtpOptions>(...)` reads `Otp__Pepper` from environment when present
  - `services.AddSingleton<IOtpHasher, Argon2OtpHasher>();`
- OTP store selection:
  - Testing:InMemory=true → `InMemoryOtpStore`
  - Otherwise → Redis via `IConnectionMultiplexer` and `RedisOtpStore`
- Sender selection:
  - If ACS configured → `AcsOtpSender`
  - Else → `ConsoleOtpSender` (writes OTP to console for local development)

### Controller behavior
- `AuthController`:
  - Start
    - Reuses unexpired plaintext OTP only for legacy values (non-hashed); otherwise generates a new code
    - Stores `OtpHash:...` when `HashingEnabled=true`, else plaintext (testing/legacy)
    - Sends the OTP using primary channel (email or phone) with console fallback
  - Verify
    - Reads stored value; if begins with `OtpHash:` → use `IOtpHasher.Verify`
    - Else (legacy plaintext) → constant-time comparison via `CryptographicOperations.FixedTimeEquals`
    - On success: deletes the OTP entry and issues a cookie auth ticket
    - Redirects: Accepts an optional `ReturnUrl` but validates it server-side with `Url.IsLocalUrl`. The API responds with `{ nextUrl: "/chat" | <safe local path> }`. The client navigates to this `nextUrl`.

### Login page behavior
- `GET /login`: If already authenticated and a `ReturnUrl` query param is present, the server validates it with `Url.IsLocalUrl` and performs `LocalRedirect` to a safe path (default `/chat`).
- Client script exposes a sanitized `window.__returnUrl` for UX purposes, but the server remains the source of truth.

### Security considerations
- OTP hashing: Pepper is required for meaningful hashing; use a high-entropy Base64 value (>= 32 bytes) per environment. Keep `HashingEnabled=true` in all non-test environments. The stored format is versioned to allow future upgrades without breaking verification.
- OTP attempt rate limiting: Per-user failed verification attempts are tracked in Redis (`otp_attempts:{userName}`) with atomic counters. After reaching the threshold (default: 5 attempts), verification requests are blocked for the TTL duration (5 minutes, synchronized with OTP expiry). This prevents brute-force attacks while allowing legitimate retries for typos. The implementation uses fail-open strategy on Redis errors to maintain availability.
- Redirect validation: All redirect targets are validated server-side using `Url.IsLocalUrl`. The client uses server-issued `nextUrl` values for navigation after verification.
- DOM XSS hardening: UI rendering avoids `innerHTML` when dealing with user-derived content; code uses `textContent` and element creation.
- Rate limiting: Multi-layer protection includes per-IP endpoint rate limiting (20 req/5s) and per-user OTP attempt limiting (5 attempts/5 min).
- Log forging mitigation: `RequestTracingMiddleware` sanitizes request method and path before logging.

## Observability
- Domain counters (Meter `Chat.Web`): `chat.otp.requests`, `chat.otp.verifications`, `chat.otp.verifications.ratelimited`, plus chat-centric metrics.
- OpenTelemetry exporters are chosen in priority order: Azure Monitor (Production) → OTLP → Console. Serilog OTLP sink is enabled only when `OTel__OtlpEndpoint` is present; otherwise logs remain on console.

## Cosmos messages retention (TTL)
When Cosmos DB repositories are used, the messages container's `DefaultTimeToLive` is managed at startup to match configuration:

- Option: `Cosmos:MessagesTtlSeconds` (nullable integer)
  - `null` or unset/empty → TTL disabled (container DefaultTimeToLive cleared)
  - `-1` → TTL enabled but items never expire by default (Cosmos semantics)
  - `> 0` seconds → items expire after the configured lifetime
- Reconciliation: The application reads the current container properties and updates `DefaultTimeToLive` when it differs from the configured value, including clearing it when disabled. This ensures drift correction when config changes between deployments.

## Localization

### Goals
- Support multiple markets with natural, idiomatic translations
- Enable culture-specific user experiences without code changes
- Provide client-side access to localized strings
- Allow culture selection via cookie or Accept-Language header

### Supported Markets (9 total)
The application supports the following cultures with complete translations:

| Culture Code | Language | Region | Notes |
|--------------|----------|--------|-------|
| `en` | English | Default | Fallback culture |
| `pl-PL` | Polish | Poland | |
| `de-DE` | German | Germany | Formal style |
| `cs-CZ` | Czech | Czech Republic | Informal friendly |
| `sk-SK` | Slovak | Slovakia | |
| `uk-UA` | Ukrainian | Ukraine | Modern Ukrainian, Cyrillic |
| `be-BY` | Belarusian | Belarus | Proper Belarusian, Cyrillic |
| `lt-LT` | Lithuanian | Lithuania | Baltic language, Latin script |
| `ru-RU` | Russian | Russia | Standard contemporary Russian, Cyrillic |

### Components

**Resource Files** (`src/Chat.Web/Resources/`)
- `SharedResources.resx` - English (default/fallback)
- `SharedResources.[locale].resx` - Translated resources for each supported culture
- Contains 60+ strings covering:
  - Application common (Loading, Error, Retry, Search)
  - Chat interface (ChatRooms, MessageInputPlaceholder, Reconnecting)
  - Authentication/OTP (SendCode, Verify, SixDigitCode)
  - Date/Time (Today, Yesterday, AM/PM)
  - Error messages (40+ error/validation strings)
  - Email/SMS templates
  - Validation messages

**LocalizationController** (`src/Chat.Web/Controllers/LocalizationController.cs`)
- REST endpoint: `GET /api/localization/strings`
- Returns JSON object with all client-needed translations for current culture
- Response cached (1 hour) with variance on `Cookie` and `Accept-Language` headers
- Used by JavaScript to populate `window.i18n` object

**Startup Configuration** (`src/Chat.Web/Startup.cs`)
```csharp
services.AddLocalization(); // Resources resolved from namespace path
services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { /* 9 cultures */ };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    
    // Priority: Cookie > Accept-Language > Default
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});
```

**Culture Resolution Priority**
1. **Cookie** (`CookieRequestCultureProvider`) - Explicit user preference stored in `.AspNetCore.Culture` cookie
2. **Accept-Language Header** (`AcceptLanguageHeaderRequestCultureProvider`) - Browser language preference
3. **Default** - Falls back to `en` if no match found

### Client Integration

**JavaScript Initialization** (`wwwroot/js/login.js`)
```javascript
// Fetch translations for current culture
const response = await fetch('/api/localization/strings');
const translations = await response.json();
window.i18n = translations;

// Use in UI
document.querySelector('.loading').textContent = window.i18n.Loading;
```

**Culture Switcher**
- UI component on login page allows culture selection
- Sets `.AspNetCore.Culture` cookie with format: `c=CULTURE|uic=CULTURE`
- Page reload applies new culture across all resources

**Razor Page Usage**
```csharp
@inject IStringLocalizer<SharedResources> Localizer

<h1>@Localizer["AppTitle"]</h1>
<p>@Localizer["AppDescription"]</p>
```

### Translation Approach
All translations focus on natural, idiomatic language rather than literal machine translations:
- **German**: Formal business style ("Sie" form throughout)
- **Czech/Slovak**: Informal friendly tone appropriate for chat
- **Ukrainian**: Modern Ukrainian avoiding Russianisms
- **Belarusian**: Proper Belarusian, distinct from Russian
- **Lithuanian**: Natural Baltic phrasing with proper diacritics

Examples:
- "What's on your mind?" (message placeholder)
  - German: "Was möchten Sie mitteilen?" (formal)
  - Czech: "Co vás napadá?" (friendly)
  - Polish: "Co Ci chodzi po głowie?" (idiomatic)
  - Ukrainian: "Що у вас на думці?" (natural)

### Testing
Comprehensive localization test coverage (`tests/Chat.Tests/LocalizationTests.cs`):
- 55 total tests covering all 9 markets
- Tests per locale:
  - App translations (AppTitle, AppDescription, Loading)
  - UI strings (Error, ChatRooms, SignInToContinue)
  - Authentication strings (SendCode, Verify, SixDigitCode)
  - All 60+ required keys validation
  - Parameterized string formatting (e.g., "Who's Here ({0})")
- Culture-specific test execution using `CultureInfo.CurrentCulture` and `CultureInfo.CurrentUICulture`
- Verifies actual `.resx` file contents match expected translations

Test results: **55/55 passing** ✅

### Future Enhancements
- User preference persistence (database-backed culture choice)
- Additional markets as needed
- RTL (right-to-left) layout support for Arabic/Hebrew
- Pluralization rules for count-based strings
- Date/time format localization
# Architecture

This section captures core components and runtime flows beyond OTP specifics.

## Current Overview
- **Framework**: ASP.NET Core (Razor Pages + minimal MVC endpoints + SignalR Hub)
- **Real-time Transport**: SignalR (in-process hub).
- **Persistence**: Azure Cosmos DB with custom repository pattern (CosmosUsersRepository, CosmosRoomsRepository, CosmosMessagesRepository). In-memory repositories available for testing.
- **Authentication**: Passwordless one-time passcode (OTP) flow. Short-lived OTP codes stored in Redis; successful verification establishes cookie-auth session.
- **Redis Usage**: Only for OTP storage (key prefix `otp:`) with TTL; no chat message caching or SignalR backplane configured yet.
- **Front-End**: Vanilla JavaScript modules (`chat.js`, `site.js`, `login.js`) referenced directly by Razor pages. Optional esbuild step can produce minified bundles in `wwwroot/js/dist/` for production testing.
- **Messaging Model**: All chat messages are sent exclusively through the SignalR hub (`ChatHub`).
- **Removed Feature**: Private/direct messaging (`/private(user)`) removed; only room-based broadcast remains.
- **Client Reliability**: Outbox queue with sessionStorage persistence for messages typed while disconnected or during (re)join. Queue flushes automatically after join / reconnect.
- **Optimistic UI**: Messages render immediately with temporary local metadata and reconcile on authoritative broadcast via correlation ID.
- **Background Connection Behavior**: Infinite reconnect policy with exponential backoff, extended server timeout (~240s) and keep-alives (~20s) to tolerate background tab throttling; proactive ensureConnected on `visibilitychange` and `online` events.
- **Attention UX**: When a new message arrives and the tab is hidden, the document title blinks until the tab is visible again.
- **Telemetry / Observability**: Activity/trace correlation through custom fetch wrapper capturing `X-Trace-Id`; client emits structured telemetry events (join attempts, sends, queue flush metrics); server leverages `ActivitySource` spans in hub operations.

## Key Components
| Layer | Component | Responsibility |
|-------|-----------|----------------|
| Client | `chat.js` | Connection lifecycle (connect/join/reconnect), outbox queue, optimistic sends, reconciling broadcasts, telemetry emission. |
| Client | Read receipts UI | Debounced mark-as-read when timeline bottom is reached and after join/reconnect by scanning visible messages; updates per-message ReadBy badges. |
| Client | `site.js` | General UI behaviors (sidebar toggles, OTP modal workflow, tooltips, message actions UI). |
| Client | `login.js` | OTP authentication flow on `/login` page; culture switcher; localization initialization. |
| Real-time | `ChatHub` | Single entry-point for sending messages and joining rooms. Normalizes routing, enriches tracing, broadcasts canonical message DTOs. Uses Context.Items for per-connection state (user, room). |
| Presence | `IPresenceTracker` / `RedisPresenceTracker` | Tracks user presence across instances using Context.Items (local) + Redis hash (distributed snapshot). |
| Background | `UnreadNotificationScheduler` | Schedules per-message delayed unread checks; sends notifications to recipients excluding sender/readers. |
| Services | `NotificationSender` / `AcsOtpSender` | Formats notification payloads and delivers them via email/SMS; enforces notification subject/body contract while preserving OTP formatting. |
| Localization | `LocalizationController` | API endpoint (`/api/localization/strings`) providing JSON translations for current culture; cached with `Accept-Language` header variance. |
| Localization | `SharedResources` | Resource files (`SharedResources.[locale].resx`) containing 60+ translated strings for 9 supported markets. |
| Auth | Auth Controllers / OTP API | `start` (generate/store OTP in Redis), `verify` (validate OTP, issue auth cookie), `logout`. |
| Data | `ApplicationDbContext` & EF migrations | Stores Users, Rooms, Messages. |
| OTP Store | `RedisOtpStore` | Wrapper over StackExchange.Redis for code set/get/delete with TTL. |
| Config | `RedisOptions` | Connection string, DB index, OTP TTL seconds. |
| Build | esbuild tasks (VS Code tasks.json) | Produce minified bundles consumed by layout. |

## SignalR Role
SignalR provides the real-time bi-directional communication channel between browser clients and the server, handling:
- Hub method invocation (client → server: send/join operations)
- Broadcast fan-out scoped to room groups
- Connection lifecycle events used to trigger auto-join and queued message flush
- Basic ordering within a single hub instance (optimistic reconciliation on client covers timing gaps)

Client read-marking is idempotent and safe across reconnects: the browser deduplicates per-session with a small cache and the server persists unique readers per message. No bulk API is required; a future optimization could add a `markReadMany` hub method if needed.

Azure SignalR is used when not running in Testing:InMemory mode; otherwise in-process SignalR is used. No Redis backplane is configured; multi-instance scale-out could use Redis backplane as an alternative.

## Redis Role
Redis serves three purposes:
1. **OTP storage**: Key pattern `otp:{userName}` with versioned Argon2id hash (format `OtpHash:v2:argon2id:...`) and TTL (default 300s)
2. **OTP attempt rate limiting**: Key pattern `otp_attempts:{userName}` with atomic INCR counter and TTL (default 300s) to prevent brute-force attacks
3. **Presence tracking**: Single hash key `presence:users` stores current user presence for cross-instance snapshot queries

Operations:
- OTP: set, get, remove via `RedisOtpStore` with retry + cooldown guards
- OTP Attempts: atomic INCR with conditional EXPIRE (TTL set only on first attempt) via `RedisOtpStore`
- Presence: HSET/HGET/HDEL via `RedisPresenceTracker` for distributed user state

Potential future roles: SignalR backplane, rate limiting, hot message cache.

## Runtime Flows
### OTP Authentication
1. Client POSTs `/api/auth/start` (generate + store OTP in Redis)
2. User receives/displayed code out-of-band via ACS (email/SMS) when configured; otherwise written to console for local development
3. Client POSTs `/api/auth/verify` with code
   - Server checks failed attempt count (`otp_attempts:{userName}`)
   - If count >= threshold (default: 5), request is blocked (401 Unauthorized)
   - Server validates OTP hash
   - On failure: increment attempt counter with TTL (5 minutes), return 401
   - On success: issue auth cookie, clear attempt counter
4. Client opens SignalR connection and auto-joins a room; outbox flush begins

**Security**: Multi-layer rate limiting (per-IP endpoint limiting + per-user attempt limiting) with OpenTelemetry metrics (chat.otp.verifications.ratelimited) to prevent brute-force attacks.

### Auto-Join & Queue Flush
- Determine target room (user default, only room, or first) then invoke hub join
- Drain sessionStorage queue FIFO, sending each with preserved correlationId

### Optimistic Send
1. User action → allocate correlationId + temporary placeholder
2. If connected: send through hub; if not: enqueue
3. Hub persists and broadcasts canonical message (includes correlationId)
4. Client reconciles & finalizes

### Reconnect Handling
- Messages composed offline accumulate in queue
- On reconnection + join success, queued messages flush automatically
- Proactive reconnect is attempted on `visibilitychange` (when becoming visible) and `online` events

### Post-join/read reconciliation (viewport-based)
1. After a successful join or reconnect, the client triggers a debounced scan of the message list.
2. It computes which messages are currently visible in the viewport, excludes messages authored by the current user, and ignores any ids it already marked in this session.
3. For each remaining id, it invokes the hub `markRead(id)`. Calls are debounced (300ms) and idempotent server-side.
4. Upon `messageRead` broadcasts, the UI updates per-message badges and any per-room unread counters.

### Unread notification scheduling
1. On message send, the server schedules a delayed check for that message using `UnreadNotificationScheduler`.
2. After `Notifications:UnreadDelaySeconds` (default 60), the scheduler queries the message state.
3. If the message is still unread by all recipients, it computes recipients:
  - Primary: `room.users` when present
  - Fallback: users whose `fixedRooms` contains the room name
  - Excludes: the sender and any users in `message.readBy`
4. `NotificationSender` sends one notification per recipient through the configured channels.

Formatting contract:
- Email subject: `New message`
- Email body: `New message in #<room>`
- SMS body: `New message in #<room>`
- No message text is included to avoid leaking content through notifications.

### Presence tracking
The application tracks which users are currently online and in which rooms using a hybrid approach:

1. **Per-connection state (Context.Items)**:
   - When a user connects, `OnConnectedAsync` stores `UserViewModel` in `Context.Items["UserProfile"]`
   - Current room is tracked in `Context.Items["CurrentRoom"]`
   - This provides instant access in hub methods (SendMessage, MarkRead) without Redis queries
   - Automatically cleaned up when connection closes

2. **Distributed snapshot (Redis)**:
   - Single hash key: `presence:users`
   - Hash fields: `{userName}` → JSON-serialized `UserViewModel` with `CurrentRoom`
   - Updated on: Join (room change), OnConnectedAsync (initial), OnDisconnectedAsync (cleanup)
   - TTL: 10 minutes, refreshed on updates
   - Used by: GetUsers (cross-instance queries), presence API endpoint

3. **Multi-instance coordination**:
   - `_UserConnectionCounts` (ConcurrentDictionary) tracks per-instance connection counts
   - Only removes from Redis when last connection for that user (on this instance) closes
   - SignalR groups handle cross-instance message broadcasting

4. **Interfaces**:
   - `IPresenceTracker`: Simplified interface (4 methods: SetUserRoomAsync, GetUserAsync, RemoveUserAsync, GetAllUsersAsync)
   - `RedisPresenceTracker`: Production implementation using Redis hash operations (no key scanning)
   - `InMemoryPresenceTracker`: Test/dev implementation using ConcurrentDictionary

Benefits:
- Fast: No Redis queries in hot path (SendMessage, MarkRead)
- Simple: Context.Items is single source of truth per connection
- Scalable: Hash operations instead of key scanning
- Reliable: Per-connection scope, automatic cleanup, TTL safety net

## Build & Front-End Delivery
- Source JS under `wwwroot/js/` is used directly in development and referenced by pages. Optional build tasks can create minified bundles in `wwwroot/js/dist/` for production testing; pages do not reference `dist` by default.

## Observability & Telemetry
- Server Activities wrap hub operations; `X-Trace-Id` header surfaces trace id to client
- Client logs join attempts, send outcomes, queue flush metrics; correlates with trace id

## Scaling Considerations
| Concern | Current State | Scale-out Path |
|---------|---------------|----------------|
| Real-time fan-out | Single hub instance | Add Redis backplane or Azure SignalR Service |
| Message persistence | Single EF DB | Shard or move to partitioned store |
| OTP store | Single Redis | Managed/clustered Redis |
| Outbox durability | sessionStorage | IndexedDB or server-side queue |
| Presence tracking | Redis hash (single key) | Sharded Redis or dedicated presence service |

## Security Notes
- **HTTP Strict Transport Security (HSTS)**: Production-ready HSTS configuration protects against protocol downgrade attacks:
  - 1-year max-age (31536000 seconds) extends protection window from default 30 days
  - `Preload` directive enabled for HSTS preload list eligibility (https://hstspreload.org/)
  - `IncludeSubDomains` enabled to protect all subdomains
  - Only applied in Production environment (not in test mode via `Testing:InMemory`)
  - Configured in `Startup.ConfigureServices` using `services.AddHsts()` with custom options
  - Activated in `Startup.Configure` via `app.UseHsts()` (Production only)
  - Expected header: `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`
  - Mitigates first-visit vulnerability and man-in-the-middle attacks
  - Certificate renewal must remain automated (Azure App Service handles this automatically)
- OTP codes are stored hashed by default (Argon2id + salt + pepper). Legacy/plaintext verification is supported only when explicitly enabled for testing and uses constant-time comparison. Provide a high-entropy Base64 pepper per environment via `Otp__Pepper`.
- Correlation IDs are opaque UUIDs (avoid embedding user data)
- OTP endpoints are rate-limited.
 - Exception handling: Cosmos message `MarkRead` now rethrows with contextual information (message id and sanitized partition/room) instead of logging and rethrowing, aligning with static analysis guidance and improving diagnostics without exposing PII.

## Future Roadmap (Prioritized)
1. Typing indicators
2. Backplane scale-out metrics & multi-instance benchmarks
3. Enhanced pagination UX/accessibility
4. Additional OTP anti-abuse policies (per-user/IP counters in Redis)

## Glossary
- **CorrelationId**: UUID bridging optimistic send and server broadcast
- **Outbox Queue**: Client sessionStorage FIFO of unsent messages
- **OTP**: Short-lived passcode enabling passwordless login
- **Hub**: SignalR abstraction for connections, groups, messaging

## Data schemas

This section documents the canonical shapes for the three core entities used by the application: user, room, and message.

Notes on storage:
- The domain models use custom repository interfaces (IUsersRepository, IRoomsRepository, IMessagesRepository) with Cosmos DB and in-memory implementations.
- Cosmos repositories use Microsoft.Azure.Cosmos SDK directly with custom query building.
- Admin tooling may emit slightly different field names; compatibility rules are noted per entity.

### User

Purpose: Identity for OTP login, room authorization, and message attribution.

Canonical fields:
- id: string (stable user key; example: "alice")
- userName: string (login/display identity; often same as id)
- fullName: string | null
- avatar: string | null (URL or slug; optional)
- enabled: boolean (default true) — determines if the user is allowed to sign in
- email: string | null
- mobileNumber: string | null (E.164 recommended)
- fixedRooms: string[] (room names the user may join; enforced server-side)
- defaultRoom: string | null (preferred room to auto-join; derived when not provided)

Relationships (relational model):
- Messages (one-to-many via Message.FromUser)
- Rooms (navigation-only convenience; joining is governed by FixedRooms, not a join table)

Cosmos compatibility:
- Some documents may store `rooms` instead of `fixedRooms`. The repository accepts either; when both present, `fixedRooms` wins.
- If `defaultRoom` is missing, the app derives it: if there is exactly one allowed room → that one; otherwise the first name in alphabetical order.

Example (logical JSON shape):
```
{
  "id": "alice",
  "userName": "alice",
  "fullName": "Alice A.",
  "avatar": null,
  "enabled": true,
  "email": "alice@example.com",
  "mobileNumber": "+12065551234",
  "fixedRooms": ["general", "ops"],
  "defaultRoom": "general"
}
```

### Room

Purpose: Public channel for posting messages.

Canonical fields:
- id: integer (stable numeric id)
- name: string (room key and display name; examples: "general", "ops", "random")
 - users: string[] (optional helper; list of user names present in the room document)

Relationships:
- Messages (one-to-many via Message.ToRoomId)

Cosmos compatibility:
- Some documents may have a non-numeric `id` (string). The repository computes a stable numeric id for in-app use (deterministic hash) when needed.
- Legacy/admin-authored docs might include an `admin` field; the app ignores it.
 - Some documents may include a `users` array. This is a denormalized helper maintained by external admin tooling. Chat.Web treats it as read-only and does not reconcile or mutate it at runtime. The authoritative source of room membership remains `user.fixedRooms`.

Example (logical JSON shape):
```
{
  "id": 1,
  "name": "general",
  "users": ["alice", "bob"]
}
```

### Message

Purpose: Immutable text payload attributed to a user and room, with a server timestamp.

Canonical fields:
- id: integer (surrogate key, monotonic per store)
- content: string (text body)
- timestamp: string (ISO 8601 UTC)
- fromUser: object (minimal attribution; in relational form this is a foreign key; in Cosmos a denormalized projection may appear)
  - id: string
  - userName: string
- toRoomId: integer (foreign key to Room.id)
- readBy: string[] (user names who have read the message)

Relationships:
- FromUser (many-to-one)
- ToRoom (many-to-one)

Cosmos compatibility:
- The repository persists and reads messages from a container that may include additional system fields (`_ts`, `_etag`, etc.); these are ignored by the domain model.
- TTL can be enabled on the container; deletion is managed by Cosmos based on `DefaultTimeToLive` (see the TTL section above).

Example (logical JSON shape):
```
{
  "id": 1042,
  "content": "Hello, world",
  "timestamp": "2025-10-13T12:34:56.789Z",
  "fromUser": { "id": "alice", "userName": "alice" },
  "toRoomId": 1
}
```

---
This document reflects the current architecture: Azure SignalR in non-test mode, background-stable client connection, hashed OTP storage, and OpenTelemetry-first observability.
