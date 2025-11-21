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

### 3.1 Container: `messages`
| Aspect | Value |
|--------|-------|
| Partition Key | `/roomName` |
| TTL | `defaultTtl: -1` (enabled, no expiry by default; adjustable via config) |
| Indexing | All paths included (no exclusions yet) |
| Rationale | Writes & reads are room-scoped. Partition key aligns with dominant access pattern ("get messages for room"). |

### 3.2 Container: `users`
| Aspect | Value |
|--------|-------|
| Partition Key | `/userName` |
| Unique Key | `/phoneNumber` (enforces uniqueness for phone-based OTP users) |
| Rationale | User-centric lookups and updates; per-user isolation prevents partition hotspots. |

### 3.3 Container: `rooms`
| Aspect | Value |
|--------|-------|
| Partition Key | `/name` |
| Rationale | Low cardinality but stable. Acceptable because room metadata volume is tiny; avoid coupling to message volume. |

### 3.4 Item Shape Examples
```jsonc
// messages container document
{
  "id": "msg_2025_11_21_123456789",
  "roomName": "general",
  "userName": "alice",
  "content": "Hello world",
  "sentUtc": "2025-11-21T12:34:56.789Z",
  "readBy": ["bob", "charlie"], // optional array
  "type": "text" // reserved for future message types
}

// users container document
{
  "id": "alice",          // mirrors userName as id
  "userName": "alice",
  "phoneNumber": "+15550001111", // unique key
  "upn": "alice@contoso.com",    // set on Entra ID login
  "email": "alice@contoso.com",
  "fullName": "Alice Example",
  "fixedRooms": ["general", "alerts"],
  "enabled": true,
  "tenantId": "<GUID>" // optional (Entra ID)
}

// rooms container document
{
  "id": "general",
  "name": "general",
  "displayName": "General",
  "description": "All-purpose chat room",
  "restricted": false
}
```

### 3.5 Partition Key Strategy
| Goal | Decision |
|------|----------|
| Even distribution | Many active rooms keep per-partition RU consumption balanced. |
| Targeted queries | Always filter by `roomName` or `userName` to avoid cross-partition fan-out. |
| Simplicity | Single-field hash keys; hierarchical partition keys (HPK) deferred until multi-tenant scale. |

### 3.6 Future: Hierarchical Partition Keys (HPK)
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
`defaultTtl: -1` enables TTL but no automatic expiry. Future configuration:
- Set `Cosmos:MessagesTtlSeconds` to a positive integer to auto-expire messages.
- Set to `null` or omit to disable TTL entirely (application keeps historic data).

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
Repositories (Cosmos or InMemory) expose uniform interfaces:
| Interface | Methods (Representative) | Notes |
|-----------|--------------------------|-------|
| `IMessagesRepository` | Add, QueryByRoom(roomName, paging), MarkRead | Query always scoped by partition key. |
| `IUsersRepository` | GetByUserName, GetByUpn, Upsert, GetAll | `GetByUpn` used in Entra ID login flow (strict match). |
| `IRoomsRepository` | GetByName, GetAllFixed, Upsert | Room creation restricted (no arbitrary dynamic rooms). |

In-memory implementations mimic behavior without network latency for integration tests.

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
| Large item prevention | Enforce max message length (already in validation layer). |
| Backups | Continuous backup covers point-in-time restore; document restore playbook separately. |
| Data seeding | `DataSeederService` runs only in non-in-memory mode at startup when empty. |

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
