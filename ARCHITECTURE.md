# Architecture

This document describes the high-level architecture of the SignalR-Chat application, its main components, and key runtime flows.

## Overview
- **Framework**: ASP.NET Core 9 (Razor Pages host + MVC API + SignalR Hub)
- **Real-time**: Azure SignalR Service
- **Data**: Azure Cosmos DB (containers: users, rooms, messages) partitioned for horizontal scale
- **Cache / OTP**: Redis (stores short-lived OTP codes)
- **Optional**: Azure Communication Services (Email/SMS) for OTP code delivery
- **Auth**: Cookie-based session established after OTP verification
- **Observability**: Serilog (structured logs) + OpenTelemetry Activities (console exporter) + custom request tracing middleware
- **Client**: Single vanilla JavaScript module (`wwwroot/js/chat.js`) handling state, DOM rendering, optimistic messaging, pagination, and SignalR interaction

## Component Diagram (Conceptual)
```
Browser (chat.js)
   | (HTTPS + Cookie)
   v
Chat.Web (ASP.NET Core)
   |-- Controllers (Auth, Messages, Rooms)
   |-- SignalR Hub (ChatHub)
   |-- Repositories (Cosmos*)
   |-- OTP (Redis + ACS/Console)
   |-- Observability (Serilog + OTel)

External Services:
   - Azure SignalR Service
   - Azure Cosmos DB (SQL API)
   - Azure Cache for Redis
   - Azure Communication Services (optional)
```

## Sequence: OTP Authentication Flow
```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant B as Browser
    participant A as Chat Web
    participant R as Redis (OTP)
    participant C as ACS (Email/SMS)

    U->>B: Enter userName + destination
    B->>A: POST /api/auth/start { userName, destination }
    A->>R: Store OTP code (TTL)
    alt ACS configured
        A->>C: Send code via Email/SMS
    else Console fallback
        A-->>A: Log code to console
    end
    A-->>B: 202 Accepted (started)
    U->>B: Enter received code
    B->>A: POST /api/auth/verify { userName, code }
    A->>R: Get & validate OTP
    alt Code valid
        A-->>B: 200 OK + profile (Set-Cookie auth)
        B->>A: SignalR negotiate /chatHub
        A-->>B: Connection info
        B-)A: Establish WebSocket (auth cookie)
        A-->>B: Hub welcome + initial state
    else Invalid/expired
        A-->>B: 400/401 error JSON
    end
```

## Sequence: Optimistic Message Send & Reconciliation
```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant B as Browser
    participant H as SignalR Hub
    participant S as Chat Web API
    participant DB as Cosmos (messages)

    U->>B: Type message + send
    B->>B: Generate correlationId & tempId (-1, -2 ...)
    B-->>B: Render optimistic message (pending)
    B->>H: hub.sendMessage(room, content, correlationId)
    H->>S: Persist request (room, content, user, correlationId)
    S->>DB: Insert message (assign real id + timestamp)
    DB-->>S: Ack (document stored)
    S-->>H: Message DTO {id, content, user, correlationId}
    H-->>B: Broadcast message to all clients
    B->>B: Reconcile optimistic (match correlationId -> replace temp)
    B-->>U: Show confirmed message (status cleared)
```

## Data Model Highlights
| Entity | Key | Partition Key | Notes |
|--------|-----|---------------|-------|
| User | userName | /userName | Simple profile (no password; OTP only) |
| Room | name | /name | Metadata for grouping messages |
| Message | id (GUID/assigned) | /roomName | Stored with room partition for efficient recent/older pagination |

## Pagination Strategy
1. Initial fetch: newest N messages (descending query → sorted ascending in memory)
2. Older fetch: pass `before` = oldest currently loaded message timestamp.
3. Client preprends without losing scroll position.

## Optimistic Messaging Strategy
- Client assigns temporary negative IDs + a UUID `correlationId`.
- On broadcast, matches by `correlationId`; falls back to content/negative ID if necessary.
- Removes duplicate temporary entries.

## Configuration Strategy
- `appsettings.json`: structural placeholders (no secrets)
- `appsettings.Production.json`: stricter logging
- Runtime secrets via environment variables / user secrets (`Cosmos__`, `Redis__`, `Acs__`)

## Observability
- Custom middleware starts an Activity per request; trace id returned via `X-Trace-Id` header.
- Repository calls add tags (room, counts) for diagnostics.
- Serilog request logging enriches environment + trace metadata.

## Future Enhancements
- OTLP exporter for traces / metrics
- In-memory dev fallback (optional) to run without Cosmos/Redis
- UI styling for pending optimistic messages (visual difference)
- Additional integration tests (pagination edge cases, reconnection)

---
This document will evolve alongside architectural changes.
