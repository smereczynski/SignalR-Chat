# Data Model & Storage Architecture

**Status**: P1 (Created Nov 21, 2025)
**Scope**: Cosmos DB (NoSQL), Redis (Managed Redis Enterprise), In-Memory test substitutes

This document details persistent and ephemeral data models, partition strategies, Redis key patterns, and future evolution areas. The goal is predictable performance, cost control, and operational clarity.

---
## 1. Overview
SignalR Chat uses two primary storage layers:
1. **Cosmos DB (NoSQL API)** – Durable storage for messages, users, rooms.
2. **Redis Enterprise** – Ephemeral storage for OTP codes, presence, rate limiting, transient heartbeats.

In **`Testing__InMemory=true`** mode, Cosmos and Redis are replaced by lightweight in-memory repositories to speed up local development and integration tests.

---
## 2. Cosmos DB Account Configuration
Source: `infra/bicep/modules/cosmos-db.bicep`

| Setting | Value / Strategy | Notes |
|---------|------------------|-------|
| Consistency | Session | Low latency with read-your-own-writes. |
| Regions | Single (e.g. polandcentral) | Simplified initial deployment. |
| Backup | Continuous 30d (prod); Periodic dev/staging | Cost optimization in non-prod. |
| Zone Redundancy | Staging/Prod enabled | Higher availability in critical envs. |
| Autoscale Max Throughput | 1000 (dev) / 4000 (staging/prod) | Scales RU/s automatically. |
| Public Access | Disabled when private endpoint configured | Enforced via Bicep conditional. |

---
## 3. Cosmos Database & Containers
Database name: `chat` (configurable via `Cosmos:Database`).

**Container Auto-Creation:**
- Controlled by `Cosmos:AutoCreate` setting (default: true)
- When enabled: Database and containers created automatically during `CosmosClients` initialization
- When disabled: Application expects pre-existing database and containers
- TTL settings reconciled on every startup (existing containers updated to match config)

### 3.1 Container: `messages`
| Aspect | Value |
|--------|-------|
| Partition Key | `/roomName` |
| Document ID | Random integer (1 to int.MaxValue) converted to string |
| TTL | Configurable via `Cosmos:MessagesTtlSeconds` (default: no TTL) |
| Indexing | All paths included (no exclusions yet) |
| Rationale | Writes & reads are room-scoped. Partition key aligns with dominant access pattern ("get messages for room"). |

### 3.2 Container: `users`
| Aspect | Value |
|--------|-------|
| Partition Key | `/userName` |
| Document ID | GUID (preserved on upsert, generated on first create) |
| Unique Constraint | Email used for OTP login; userName is primary identifier |
| Rationale | User-centric lookups and updates; per-user isolation prevents partition hotspots. |

### 3.3 Container: `rooms`
| Aspect | Value |
|--------|-------|
| Partition Key | `/name` |
| Document ID | Same as room name (e.g., "general", "ops") |
| Rationale | Low cardinality but stable. Acceptable because room metadata volume is tiny; avoid coupling to message volume. |

### 3.4 Item Shape Examples

**Note:** Cosmos documents use camelCase field names (different from C# models which use PascalCase). Internal DTOs (`UserDoc`, `RoomDoc`, `MessageDoc`) handle this mapping.

```jsonc
// messages container document
{
  "id": "1847293847",              // Random integer as string
  "roomName": "general",            // Partition key
  "content": "Hello world",
  "fromUser": "alice@example.com",  // Username of sender
  "timestamp": "2025-11-21T12:34:56.789Z",
  "readBy": ["bob@example.com", "charlie@example.com"] // Array of usernames (optional)
}

// users container document
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890", // GUID (preserved on upsert)
  "userName": "alice@example.com",               // Partition key, primary identifier
  "fullName": "Alice Johnson",
  "avatar": null,                                // Optional avatar URL
  "email": "alice@example.com",                  // Email for notifications
  "mobile": "+1234567890",                       // E.164 format mobile number
  "enabled": true,                               // Account status flag
  "fixedRooms": ["general", "ops"],              // Array of room names
  "defaultRoom": "general",                      // Preferred starting room
  
  // Entra ID fields (null for OTP-only users)
  "upn": "alice@contoso.com",                    // User Principal Name
  "tenantId": "12345678-1234-1234-1234-123456789abc", // Entra ID tenant GUID
  "displayName": "Alice Johnson",                // Display name from Entra ID
  "country": "US",                               // ISO 3166-1 alpha-2 code
  "region": "California"                         // State/region
}

// rooms container document
{
  "id": "general",           // Same as name
  "name": "general",         // Partition key, unique identifier
  "admin": null,             // Reserved for future admin assignment
  "users": []                // Array of usernames (denormalized for quick lookup)
}
```

### 3.5 Partition Key Strategy
| Goal | Decision |
|------|----------|
| Even distribution | Many active rooms keep per-partition RU consumption balanced. |
| Targeted queries | Always filter by `roomName` or `userName` to avoid cross-partition fan-out. |
| Simplicity | Single-field hash keys; hierarchical partition keys (HPK) deferred until multi-tenant scale. |

### 3.6 Document ID Generation
| Container | Strategy | Rationale |
|-----------|----------|-----------|
| `messages` | Random integer (1 to int.MaxValue) as string | Simple, collision-unlikely given partition scope; avoids timestamp-based hotspots. |
| `users` | GUID (generated once, preserved on upsert) | Stable identifier across updates; userName serves as lookup key via partition. |
| `rooms` | Same as room name (e.g., "general") | Natural key, human-readable, enforces uniqueness. |

**Important:** For users, the Upsert operation preserves existing document ID by querying for the user first. New users get a fresh GUID.

### 3.7 Future: Hierarchical Partition Keys (HPK)
Potential evolution for multi-tenant isolation:
```text
/tenantId /roomName   // messages
/tenantId /userName   // users
```
Benefits:
- Higher storage limit per logical partition (>20 GB).
- Efficient targeted cross-room queries within a tenant.
Constraints: Requires enabling HPK on new containers (migration strategy: dual-write + backfill).

---
## 4. Message Retention & TTL
Messages container supports configurable TTL via `Cosmos:MessagesTtlSeconds` app setting:
- **Not set or null**: TTL disabled, messages persist indefinitely
- **Positive integer**: Messages auto-expire after specified seconds (e.g., 604800 = 7 days)
- TTL reconciliation: On container creation/startup, existing TTL settings are updated to match configuration

**Implementation Details:**
- Container property `DefaultTimeToLive` is set/updated during `CosmosClients` initialization
- Existing containers have their TTL setting reconciled on every app startup
- Individual messages can override TTL using item-level `ttl` property (not currently used)

TTL Considerations:
| Scenario | Suggested TTL |
|----------|---------------|
| High-volume ephemeral chat | 7d (604800 seconds) |
| Compliance / audit required | Disable TTL |
| Demo environments | 1d (86400 seconds) |

---
## 5. Redis Data Model
Redis Enterprise cluster with single `default` database. All keys use explicit prefixes to avoid collisions.

### 5.1 Key Families
| Purpose | Pattern | Type | TTL |
|---------|---------|------|-----|
| OTP Codes | `otp:{user}` | String | Short (e.g. 5 minutes) |
| OTP Attempts | `otp_attempts:{user}` | String (count) | Rate limit window |
| Presence Hash | `presence:users` | Hash (field=userName, value=JSON) | 10 min sliding (overall hash expiry) |
| Heartbeats | `heartbeat:{user}` | String timestamp | 2 min |
| (Sliding) MarkRead limiter | In-memory only | N/A | N/A |

### 5.2 Presence Tracking (`RedisPresenceTracker`)
- Single hash `presence:users` stores serialized `UserViewModel` objects.
- Per-user heartbeat keys detect stale connections: pattern `heartbeat:{userName}` with 120s TTL.
- Cleanup service scans heartbeat keys and removes stale presence entries.

### 5.3 OTP Storage (`RedisOtpStore`)
- `otp:{user}` contains hashed or raw OTP (depending on hasher design) with TTL (configured by OTP workflow).
- Cooldown mechanism: failures arm a 10s in-process cooldown to reduce pressure on Redis after transient faults.
- Attempt counting uses `otp_attempts:{user}` key with first-write TTL for window enforcement.

### 5.4 Rate Limiting (`MarkReadRateLimiter`)
Implemented as in-memory sliding window (ConcurrentDictionary of timestamps). Intentionally **not persisted in Redis** to:
- Avoid cross-instance synchronization complexity before multi-instance scaling.
- Keep latency minimal.

Future scaling option: Move to Redis using sorted sets:
```text
ZADD ratelimit:markread:{user} <epoch-ms> <epoch-ms>
ZREMRANGEBYSCORE ratelimit:markread:{user} 0 <cutoff>
ZCOUNT ratelimit:markread:{user} <cutoff> +inf
```

---
## 6. Data Access Layer
Repositories (Cosmos or InMemory) expose uniform interfaces with **async** signatures:

| Interface | Key Methods | Notes |
|-----------|-------------|-------|
| `IMessagesRepository` | `CreateAsync`, `GetRecentByRoomAsync`, `GetBeforeByRoomAsync`, `MarkReadAsync`, `DeleteAsync` | Queries always scoped by partition key (roomName). |
| `IUsersRepository` | `GetByUserNameAsync`, `GetByUpnAsync`, `UpsertAsync`, `GetAllAsync` | `GetByUpnAsync` used for Entra ID login (case-insensitive match). |
| `IRoomsRepository` | `GetByNameAsync`, `GetByIdAsync`, `GetAllAsync`, `AddUserToRoomAsync`, `RemoveUserFromRoomAsync` | Fixed room list, no dynamic creation. |

**Current Implementation (as of 2025-12-02, Issues #28 and #66):**
- ✅ Async factory pattern: `CosmosClients.CreateAsync()` with private constructor
- ✅ All repository methods use proper async/await throughout
- ✅ Query patterns consolidated via `CosmosQueryHelper` class (reduces code duplication)
- ✅ Generic helper methods: `ExecutePaginatedQueryAsync<TDoc, T>` and `ExecuteSingleResultQueryAsync<TDoc, T>`
- ✅ Retry logic, telemetry, and error handling centralized in helpers
- ✅ **ConfigureAwait(false)** on all 60+ async operations in infrastructure code (repositories, services)
- ✅ Proper async initialization via `CosmosClientsInitializationService` (IHostedService pattern)
- 📈 **Performance**: Eliminates ALL blocking async calls and SynchronizationContext overhead

**CosmosQueryHelper Implementation Details:**
- Centralizes paginated query pattern: `while (queryIterator.HasMoreResults)` loop with retry logic
- Handles single-result queries with `FirstOrDefault()` pattern
- Integrates OpenTelemetry activity events (page count tracking, result count tagging)
- Consistent error handling with activity status tracking and structured logging
- Generic constraints ensure type safety (`where TDoc : class where T : class`)

In-memory implementations mimic behavior without network latency for integration tests (use `Task.FromResult()` pattern).

---
## 7. Multi-Tenancy & Future Evolution
Planned enhancements when introducing tenants:
| Area | Adjustment |
|------|-----------|
| Partition Keys | Switch to HPK `/tenantId /roomName` & `/tenantId /userName` |
| Isolation | Add tenantId attribute to all documents; use filtered queries enforcing tenant context. |
| Redis Namespacing | Prefix keys: `tenant:{id}:otp:{user}` etc. |
| Sharding | Evaluate adding additional Redis databases or clusters per region. |

---
## 8. Indexing Strategy
Currently: **Full indexing** on all containers. Future optimizations:
| Optimization | Trigger |
|--------------|---------|
| Exclude large unqueried arrays (e.g. `readBy`) | >50 RU read operations for large result sets |
| Composite indexes (roomName + sentUtc) | High-volume chronological queries with filters |
| Dedicated analytical container | Long-term retention / reporting needs |

Index policy changes require re-computation: schedule during low-traffic windows.

---
## 9. Operational Guidelines
| Task | Action |
|------|--------|
| Hot partition detection | Use Azure Metrics: Normalized RU Consumption per partition key (roomName). |
| Large item prevention | Enforce max message length (currently 1000 characters in validation layer). |
| Backups | Continuous backup (prod) covers point-in-time restore; Periodic backup (dev/staging). |
| Data seeding | `DataSeederService` seeds default rooms/users only when containers are empty. |
| User ID preservation | Upsert operation queries existing user by userName to preserve document ID (GUID). |
| Message ID conflicts | Random integer generation (1 to int.MaxValue) has negligible collision risk per room partition. |

---
## 10. Migration Playbooks
### 10.1 Introduce TTL
1. Decide retention (e.g. 30d).
2. Set `Cosmos:MessagesTtlSeconds=2592000` in App Service settings.
3. Deploy – TTL only applies to new writes; re-write old items or leave them unexpired.

### 10.2 Enable HPK (Future)
1. Create new containers with HPK.
2. Dual-write messages to old + new containers.
3. Backfill historic data via batch process.
4. Switch read path after parity verification.
5. Decommission legacy containers.

### 10.3 Add Tenant Isolation
1. Introduce `tenantId` column to models.
2. Update repositories to enforce tenant scoping.
3. Migrate existing data (assign default tenant).

---
## 11. Validation & Local Testing
```bash
# In-Memory (no external services)
Testing__InMemory=true dotnet test src/Chat.sln

# Azure Mode (uses real Cosmos/Redis via connection strings)
dotnet run --project src/Chat.Web --urls=https://localhost:5099
```
Check startup logs for:
- "Initializing Cosmos DB clients" messages
- Redis connection summary (endpoints, status)

---
## 12. Risks & Mitigations
| Risk | Impact | Mitigation |
|------|--------|-----------|
| Partition hotspot (single popular room) | Elevated RU cost / throttling | Split room, introduce subrooms, or HPK with secondary dimension. |
| Redis key explosion (heartbeats) | Memory churn | Short TTL (2m) and single hash for presence reduces cardinality. |
| Unbounded message growth | Storage cost | Introduce TTL or archival pipeline. |
| Missing user provisioning for Entra ID | Login failures | Operational checklist to update `upn` for new users before enabling SSO. |

---
## 13. References
- `infra/bicep/modules/cosmos-db.bicep`
- `infra/bicep/modules/redis.bicep`
- `src/Chat.Web/Services/RedisPresenceTracker.cs`
- `src/Chat.Web/Services/RedisOtpStore.cs`
- `src/Chat.Web/Services/MarkReadRateLimiter.cs`
- `src/Chat.Web/Startup.cs`

---
**Last Updated**: 2025-11-21
