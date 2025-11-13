# Architecture Overview

This document provides a high-level overview of the SignalR Chat architecture, including system components, data flow, and infrastructure design.

## System Architecture

### Runtime Architecture

```mermaid
graph TB
    subgraph "Client Layer"
        Browser[Web Browser<br/>HTML + JavaScript]
        SignalRClient[SignalR Client<br/>WebSocket/SSE]
    end
    
    subgraph "Application Layer - ASP.NET Core 9"
        RazorPages[Razor Pages<br/>/login, /chat]
        Controllers[REST Controllers<br/>API Endpoints]
        SignalRHub[SignalR Hub<br/>ChatHub]
        Middleware[Middleware<br/>Auth, Security, Logging]
        
        RazorPages --> Middleware
        Controllers --> Middleware
        SignalRHub --> Middleware
    end
    
    subgraph "Business Logic Layer"
        AuthService[OTP Service<br/>Argon2id Hashing]
        PresenceService[Presence Service<br/>Online/Offline Tracking]
        NotificationService[Notification Service<br/>Email/SMS Scheduler]
        
        Middleware --> AuthService
        Middleware --> PresenceService
        Middleware --> NotificationService
    end
    
    subgraph "Data Layer"
        MsgRepo[Message Repository]
        RoomRepo[Room Repository]
        UserRepo[User Repository]
        OtpStore[OTP Store<br/>Redis]
        
        AuthService --> OtpStore
        PresenceService --> OtpStore
        NotificationService --> MsgRepo
        SignalRHub --> MsgRepo
        SignalRHub --> RoomRepo
        SignalRHub --> UserRepo
    end
    
    subgraph "Infrastructure - Azure"
        CosmosDB[(Cosmos DB<br/>NoSQL Database)]
        Redis[(Azure Redis<br/>Cache)]
        SignalRService[Azure SignalR Service<br/>Scale-out]
        ACS[Azure Communication Services<br/>Email/SMS]
        AppInsights[Application Insights<br/>Telemetry]
    end
    
    Browser -->|HTTPS| RazorPages
    Browser -->|HTTPS| Controllers
    SignalRClient -->|WebSocket/SSE| SignalRHub
    
    MsgRepo --> CosmosDB
    RoomRepo --> CosmosDB
    UserRepo --> CosmosDB
    OtpStore --> Redis
    
    SignalRHub -.->|Optional| SignalRService
    NotificationService -.->|Optional| ACS
    Application -.->|Telemetry| AppInsights
    
    style Browser fill:#e1f5ff
    style CosmosDB fill:#ffe1e1
    style Redis fill:#ffe1e1
    style SignalRService fill:#e1ffe1
    style AppInsights fill:#fff4e1
```

### Key Components

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Web Server** | ASP.NET Core 9 | HTTP server, Razor Pages, WebSocket handling |
| **Real-time Engine** | SignalR | WebSocket hub for bidirectional communication |
| **Database** | Azure Cosmos DB (NoSQL) | Messages, rooms, users, read receipts |
| **Cache** | Azure Redis | OTP storage, rate limiting, presence tracking |
| **Authentication** | Cookie-based + OTP | Secure login with Argon2id hashing |
| **Observability** | OpenTelemetry + App Insights | Metrics, traces, logs |
| **Notifications** | Azure Communication Services | Email/SMS delivery |

## Data Flow

### Message Flow

```mermaid
sequenceDiagram
    participant User as User (Browser)
    participant Hub as SignalR Hub
    participant Repo as Message Repository
    participant DB as Cosmos DB
    participant Room as Room Members
    
    User->>Hub: SendMessage(roomId, text)
    Note over Hub: Validate auth & room membership
    
    Hub->>Repo: SaveMessageAsync()
    Repo->>DB: Create document in messages container
    DB-->>Repo: Document created (with ID)
    Repo-->>Hub: Message saved
    
    Hub->>Room: ReceiveMessage(message)
    Note over Room: Broadcast to all<br/>room members
    
    User->>Hub: MarkAsRead(messageId)
    Hub->>Repo: UpdateReadReceiptsAsync()
    Repo->>DB: Update readBy array
    
    Hub->>Room: MessageRead(messageId, userName)
    Note over Room: Notify all members<br/>of read status
```

### OTP Authentication Flow

```mermaid
sequenceDiagram
    participant User as User (Browser)
    participant Auth as Auth Controller
    participant OTP as OTP Service
    participant Redis as Redis Cache
    participant ACS as Azure Communication Services
    participant Cosmos as Cosmos DB
    
    User->>Auth: POST /api/auth/start<br/>{userName}
    Auth->>OTP: GenerateOtpAsync(userName)
    
    Note over OTP: Generate 6-digit code<br/>Hash with Argon2id
    
    OTP->>Redis: SET otp:{userName}<br/>value: hashedCode<br/>TTL: 5 minutes
    Redis-->>OTP: OK
    
    OTP->>ACS: SendSMS(phoneNumber, code)
    OTP->>ACS: SendEmail(email, code)
    ACS-->>OTP: Messages queued
    
    OTP-->>Auth: OTP sent
    Auth-->>User: 200 OK
    
    Note over User: User receives code<br/>via SMS/Email
    
    User->>Auth: POST /api/auth/verify<br/>{userName, code}
    Auth->>OTP: VerifyOtpAsync(userName, code)
    
    OTP->>Redis: GET otp:{userName}
    Redis-->>OTP: hashedCode
    
    Note over OTP: Hash input code<br/>Compare with stored hash
    
    alt Code valid
        OTP->>Redis: DEL otp:{userName}
        OTP-->>Auth: Verification success
        Auth->>Cosmos: Get user details
        Cosmos-->>Auth: User data
        Auth->>User: Set authentication cookie
        Auth-->>User: 200 OK + cookie
    else Code invalid
        OTP->>Redis: INCR otp_attempts:{userName}<br/>TTL: 5 minutes
        Note over OTP: Block after 5 attempts
        OTP-->>Auth: Verification failed
        Auth-->>User: 401 Unauthorized
    end
```

## Infrastructure Architecture

### Azure Resources

```mermaid
graph TB
    subgraph "Azure Subscription"
        subgraph "Resource Group: rg-chat-prod"
            subgraph "Networking"
                VNet[Virtual Network<br/>10.0.0.0/26]
                AppSubnet[App Service Subnet<br/>10.0.0.0/27]
                PESubnet[Private Endpoint Subnet<br/>10.0.0.32/27]
                NSG1[Network Security Group<br/>App Service]
                NSG2[Network Security Group<br/>Private Endpoints]
            end
            
            subgraph "Compute"
                AppPlan[App Service Plan<br/>P0V4 Premium<br/>Linux]
                WebApp[Web App<br/>ASP.NET Core 9<br/>Linux]
            end
            
            subgraph "Data & Cache"
                Cosmos[Cosmos DB<br/>NoSQL API<br/>Standard RU/s]
                RedisCache[Azure Redis<br/>Balanced_B5]
            end
            
            subgraph "Messaging & Scale"
                SignalR[Azure SignalR Service<br/>Standard S1]
                CommSvc[Communication Services<br/>Email + SMS]
            end
            
            subgraph "Monitoring"
                LogAnalytics[Log Analytics<br/>Workspace]
                AppInsights[Application Insights<br/>Workspace-based]
            end
            
            subgraph "Private Connectivity"
                PE_Cosmos[Private Endpoint<br/>Cosmos DB]
                PE_Redis[Private Endpoint<br/>Redis]
                PE_SignalR[Private Endpoint<br/>SignalR]
                PE_App[Private Endpoint<br/>App Service]
            end
        end
    end
    
    VNet --> AppSubnet
    VNet --> PESubnet
    AppSubnet --> NSG1
    PESubnet --> NSG2
    
    WebApp -->|VNet Integration| AppSubnet
    WebApp -->|Routes traffic via| PESubnet
    
    PE_Cosmos -->|10.0.0.36-37| Cosmos
    PE_Redis -->|10.0.0.38| RedisCache
    PE_SignalR -->|10.0.0.39| SignalR
    PE_App -->|10.0.0.40| WebApp
    
    PESubnet --> PE_Cosmos
    PESubnet --> PE_Redis
    PESubnet --> PE_SignalR
    PESubnet --> PE_App
    
    WebApp -.->|Metrics/Logs| AppInsights
    AppInsights --> LogAnalytics
    WebApp -.->|Optional SMS/Email| CommSvc
    
    style VNet fill:#e1f5ff
    style Cosmos fill:#ffe1e1
    style RedisCache fill:#ffe1e1
    style WebApp fill:#e1ffe1
    style AppInsights fill:#fff4e1
```

### Network Architecture

- **VNet**: /26 CIDR block (64 IPs)
- **Two subnets**:
  - App Service Integration (/27 = 32 IPs)
  - Private Endpoints (/27 = 32 IPs)
- **Private endpoints** with static IP allocation:
  - Cosmos DB: .36 (global), .37 (regional)
  - Redis: .38
  - SignalR: .39
  - App Service: .40
- **All traffic** routed through VNet (no public database access)

## Security Architecture

### Defense in Depth

```mermaid
graph TD
    Internet[Internet]
    
    subgraph "Perimeter Security"
        HTTPS[HTTPS Only<br/>TLS 1.2+]
        HSTS[HSTS Headers<br/>1-year max-age]
        CSP[Content Security Policy<br/>Nonce-based]
    end
    
    subgraph "Application Security"
        Auth[Cookie Authentication]
        OTP[OTP Verification<br/>Argon2id]
        RateLimit[Rate Limiting<br/>Endpoint + Per-user]
        InputVal[Input Validation<br/>Sanitization]
    end
    
    subgraph "Network Security"
        VNet[Virtual Network]
        PrivateEndpoints[Private Endpoints]
        NSG[Network Security Groups]
    end
    
    subgraph "Data Security"
        Encryption[Encryption at Rest<br/>TDE]
        EncryptionTransit[Encryption in Transit<br/>TLS]
        AccessControl[RBAC + Managed Identity]
    end
    
    Internet --> HTTPS
    HTTPS --> HSTS
    HSTS --> CSP
    CSP --> Auth
    Auth --> OTP
    OTP --> RateLimit
    RateLimit --> InputVal
    InputVal --> VNet
    VNet --> PrivateEndpoints
    PrivateEndpoints --> NSG
    NSG --> Encryption
    Encryption --> EncryptionTransit
    EncryptionTransit --> AccessControl
    
    style HTTPS fill:#ffe1e1
    style Auth fill:#ffe1e1
    style VNet fill:#e1f5ff
    style Encryption fill:#e1ffe1
```

### Security Measures

| Layer | Measures |
|-------|----------|
| **Transport** | HTTPS only, TLS 1.2+, HSTS with preload |
| **Headers** | CSP (nonce-based), X-Frame-Options: DENY, X-Content-Type-Options: nosniff |
| **Authentication** | Cookie-based, OTP with Argon2id hashing, 5-attempt lockout |
| **Authorization** | Room membership checks, user validation |
| **Input** | Sanitized logging (CWE-117), parameter validation |
| **Rate Limiting** | 5 OTP requests/min per user, 20 requests/5s per endpoint |
| **Network** | Private endpoints, VNet integration, NSGs |
| **Data** | Encryption at rest (Cosmos/Redis TDE), encryption in transit |

## Technology Stack

### Backend
- **Framework**: ASP.NET Core 9.0
- **Real-time**: SignalR (WebSocket + Server-Sent Events)
- **Database**: Azure Cosmos DB (NoSQL, serverless → standard provisioned)
- **Cache**: Azure Redis (Enterprise tier)
- **Authentication**: Cookie-based with OTP
- **Hashing**: Argon2id (memory: 64MB, iterations: 4)

### Frontend
- **UI**: Razor Pages with server-side rendering
- **JavaScript**: Vanilla ES6+ (no framework)
- **CSS**: Bootstrap 5.3.8
- **Real-time Client**: @microsoft/signalr 9.0.6

### Infrastructure
- **IaC**: Azure Bicep
- **CI/CD**: GitHub Actions (federated identity)
- **Monitoring**: OpenTelemetry + Azure Application Insights
- **Deployment**: Azure App Service (Linux, .NET 9)

## Scalability

### Horizontal Scaling
- **Azure SignalR Service**: Scale to 1000s of concurrent connections
- **App Service**: Multiple instances with load balancer
- **Cosmos DB**: Partitioned by roomId for even distribution
- **Redis**: Clustered for high availability

### Vertical Scaling
| Environment | App Service | Cosmos DB | Redis | SignalR |
|-------------|-------------|-----------|-------|---------|
| **Dev** | P0V4 (1 vCore) | Serverless | Balanced_B1 | Standard_S1 |
| **Staging** | P0V4 (1 vCore) | 1000 RU/s | Balanced_B3 | Standard_S1 |
| **Production** | P0V4 (1 vCore) | 4000 RU/s | Balanced_B5 | Standard_S1 |

## See Also

- [Data Model](data-model.md) - Cosmos DB schema and Redis keys
- [Security Architecture](security.md) - Detailed security design
- [Architecture Decisions (ADRs)](decisions/) - Key design decisions
- [Diagrams](diagrams/) - Additional architecture diagrams

---

**Next**: [Data Model](data-model.md) | [Back to docs](../README.md)
