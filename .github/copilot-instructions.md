# GitHub Copilot Instructions for SignalR Chat

This file provides context and guidelines for GitHub Copilot when working in this workspace.

---

## General Guidelines

### Tool Usage Preferences

1. **MCP Servers First**: Always prefer using Model Context Protocol (MCP) servers before falling back to CLI commands. MCP servers provide structured, reliable access to services and APIs.

2. **API Version Selection**: Always check for the newest API version in proper reference documentation (e.g., Azure Bicep, Azure ARM). Prefer stable API versions over preview versions, but if a preview API has a specific method required for the task, propose using it with appropriate justification.

3. **GitHub CLI Usage**: When using `gh` CLI:
   - **First preference**: Use `gh api` subcommand with direct API methods
   - **Second preference**: Use subcommands with `--json` flag for structured output
   - Avoid plain text output that requires parsing

4. **File Operations**: Never create files using CLI commands (e.g., `echo >`, `cat >`, `touch`). Always use Visual Studio Code's built-in methods for file creation and editing.

5. **Output Handling**: Never return agent task output to a file (e.g., as markdown report) unless explicitly requested by the user. Present results directly in conversation.

6. **Task Persistence**: Always iterate and explore more options to resolve tasks completely. Don't give up prematurely - the user will cancel the task if needed.

### Git and GitHub Workflow
**CRITICAL**: All contributions using Git and GitHub MUST be aligned with [CONTRIBUTING.md](../CONTRIBUTING.md).

Key requirements:
- Use conventional commit messages: `<type>: <description>` (feat, fix, docs, test, refactor, perf, chore)
- Create feature branches: `feature/your-feature-name` or `fix/bug-description`
- **Never merge branches locally** - always create a Pull Request on GitHub for review
- Follow the PR process outlined in CONTRIBUTING.md
- Ensure all tests pass before committing
- Keep PR scope focused (one feature/fix per PR)

Example commits:
```bash
git commit -m "feat: add message editing capability"
git commit -m "fix: resolve race condition in read receipts"
git commit -m "docs: update deployment guide for Azure"
git commit -m "test: add integration tests for OTP flow"
```

---

## Project Context

### What This Project Is
SignalR Chat is a **production-ready real-time chat application** built with:
- **Backend**: ASP.NET Core 9, SignalR, C# 13
- **Frontend**: Razor Pages, vanilla JavaScript (ES6+), Bootstrap 5
- **Data**: Azure Cosmos DB (NoSQL), Redis (caching/rate limiting)
- **Cloud**: Azure App Service (Linux), Azure SignalR Service, Azure Communication Services
- **Observability**: OpenTelemetry, Application Insights, structured logging
- **Security**: OTP authentication (Argon2id), rate limiting, security headers (CSP, HSTS)

### What This Project Is NOT
- ❌ No direct messages (DMs) - only fixed chat rooms
- ❌ No message editing/deletion
- ❌ No user registration - fixed users (alice, bob, charlie, dave, eve)
- ❌ No file uploads or rich media
- ❌ No jQuery - use vanilla JavaScript
- ❌ No Windows deployment - Azure App Service on Linux only

---

## Code Style and Standards

### C# Code
- Follow [Microsoft C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use **4 spaces** for indentation
- Naming: **PascalCase** for classes/methods, **camelCase** for local variables/parameters
- Use nullable reference types: `string?` for nullable strings
- Prefer `async`/`await` for I/O operations
- Use `ILogger<T>` for logging, never `Console.WriteLine` in production code
- Use dependency injection for services

Example:
```csharp
public class MessageService
{
    private readonly IMessageRepository _messageRepository;
    private readonly ILogger<MessageService> _logger;

    public MessageService(IMessageRepository messageRepository, ILogger<MessageService> logger)
    {
        _messageRepository = messageRepository;
        _logger = logger;
    }

    public async Task<Message?> GetMessageAsync(string messageId)
    {
        _logger.LogDebug("Retrieving message {MessageId}", messageId);
        return await _messageRepository.GetByIdAsync(messageId);
    }
}
```

### JavaScript Code
- Use **modern ES6+** syntax (const/let, arrow functions, template literals)
- Use **2 spaces** for indentation
- Naming: **camelCase** for variables/functions, **PascalCase** for classes
- NO jQuery - use vanilla JavaScript and Web APIs
- Use `async`/`await` for asynchronous operations
- Use SignalR JavaScript client for real-time communication

Example:
```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/chathub")
  .withAutomaticReconnect()
  .build();

async function sendMessage(roomId, content) {
  try {
    await connection.invoke("SendMessage", roomId, content);
  } catch (err) {
    console.error("Error sending message:", err);
  }
}
```

### Bicep Infrastructure as Code
- Use **2 spaces** for indentation
- Always add `@description()` annotations for parameters
- Use secure parameters for secrets: `@secure() param connectionString string`
- Group related resources with comments
- Use **Linux** for App Service (not Windows)
- Use double underscore (`__`) notation for hierarchical app settings (not colon `:`)

Example:
```bicep
@description('The name of the App Service')
param appName string

@description('Cosmos DB connection string')
@secure()
param cosmosConnectionString string

resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      appSettings: [
        {
          name: 'Cosmos__Database'  // Use __ not :
          value: 'chat'
        }
      ]
    }
  }
}
```

### Testing
- Write **unit tests** for business logic, utilities, services
- Write **integration tests** for API endpoints, SignalR hubs, auth flows
- Aim for **>80% coverage** on new code
- Use xUnit framework
- Use meaningful test names: `MethodName_Scenario_ExpectedBehavior`

Example:
```csharp
[Fact]
public async Task SendMessage_ValidInput_ReturnsSuccess()
{
    // Arrange
    var service = new MessageService(_mockRepository.Object, _mockLogger.Object);
    
    // Act
    var result = await service.SendMessageAsync("room1", "user1", "Hello");
    
    // Assert
    Assert.True(result.Success);
}
```

---

## Project Structure

```
src/Chat.Web/
├── Controllers/        # REST API endpoints (MessagesController, RoomsController)
├── Hubs/              # SignalR hubs (ChatHub - real-time messaging)
├── Pages/             # Razor Pages (Login, Index, Error pages)
├── Services/          # Business logic (MessageService, PresenceService)
├── Repositories/      # Data access (CosmosDB repositories)
├── Middleware/        # Request pipeline (LogSanitizer, SecurityHeaders)
├── Models/            # Domain models (Message, Room, User, ReadStatus)
├── Options/           # Configuration classes (CosmosOptions, RedisOptions)
├── Utilities/         # Helpers, extensions
├── Resources/         # Localization .resx files (9 languages)
└── wwwroot/           # Static files (JS, CSS, libraries)

tests/
├── Chat.Tests/              # Unit tests (utilities, services)
├── Chat.IntegrationTests/   # Integration tests (ChatHub, auth flows)
└── Chat.Web.Tests/          # Web/security tests (health endpoints, headers)

infra/bicep/
├── main.bicep               # Main infrastructure orchestration
└── modules/                 # Modular resources (app-service, cosmos-db, redis, signalr)

docs/
├── getting-started/         # Installation, configuration, quickstart
├── architecture/            # System design, diagrams, decisions
├── deployment/              # Azure deployment, Bicep, CI/CD, Linux migration
├── features/                # Authentication, presence, i18n, sessions
├── development/             # Local setup, testing, debugging
├── operations/              # Monitoring, diagnostics, health checks
└── reference/               # API, configuration, telemetry
```

---

## Key Technologies and Patterns

### ASP.NET Core 9
- Minimal API approach where appropriate
- Dependency injection for all services
- Configuration via `appsettings.json` and environment variables
- Health checks at `/health` and `/healthz`
- Cookie-based authentication (no JWT for simplicity)

### SignalR
- Hub: `ChatHub` - real-time messaging, presence, typing indicators
- JavaScript client: `microsoft-signalr` (in `wwwroot/lib/`)
- Connection management: automatic reconnection, connection state tracking
- Methods: `SendMessage`, `JoinRoom`, `LeaveRoom`, `UpdateTypingStatus`, `MarkAsRead`

### Azure Cosmos DB
- NoSQL database for messages, rooms, users, read status
- Partition key strategy: `roomId` (messages), `id` (rooms/users)
- Use **hierarchical partition keys** where appropriate
- Repository pattern for data access
- Containers: `messages`, `rooms`, `users`

### Redis
- OTP code storage (short TTL)
- Rate limiting (OTP attempts, message sending)
- Distributed session state (when not using in-memory)
- Use `StackExchange.Redis` client

### Azure Configuration
- **Linux App Service** only (not Windows)
- App settings use **double underscore** (`__`) for hierarchy: `Cosmos__Database`, `Acs__EmailFrom`
- Connection strings: Cosmos, Redis, SignalR, ACS stored as connection strings (not app settings)
- Environment variables: `ASPNETCORE_ENVIRONMENT` (Development/Production)

---

## Security Best Practices

### Authentication
- OTP-based authentication (no passwords)
- Argon2id hashing for OTP codes
- Rate limiting: max 5 OTP attempts per 15 minutes
- Cookie-based sessions (secure, httpOnly, sameSite)

### Security Headers
- Content Security Policy (CSP) with nonces for inline scripts
- HSTS (HTTP Strict Transport Security)
- X-Frame-Options: DENY
- X-Content-Type-Options: nosniff
- Referrer-Policy: strict-origin-when-cross-origin

### Input Validation
- Sanitize user input before logging (PII redaction)
- Validate message length (max 1000 characters)
- Validate room IDs against allowed list
- Use anti-forgery tokens for forms

### Secrets Management
- NEVER commit secrets to Git
- Use Azure Key Vault for production secrets
- Use `.env.local` for local development (gitignored)
- Use `@secure()` parameters in Bicep

---

## Configuration

### Hierarchical Configuration
ASP.NET Core uses `:` (colon) notation in C# code:
```csharp
Configuration["Cosmos:Database"]
Configuration["Acs:EmailFrom"]
```

But Azure App Service on **Linux** requires `__` (double underscore) in app settings:
```bicep
{
  name: 'Cosmos__Database'  // Not Cosmos:Database
  value: 'chat'
}
```

ASP.NET Core automatically translates `__` → `:` when reading configuration.

### Environment Variables
Common environment variables:
- `ASPNETCORE_ENVIRONMENT`: Development, Staging, Production
- `Testing__InMemory`: "true" for in-memory mode (no Azure dependencies)
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: Application Insights connection string

---

## Observability

### Logging
- Use `ILogger<T>` for structured logging
- Log levels: Debug (verbose), Information (key events), Warning (recoverable), Error (failures)
- Include context: user ID, room ID, message ID, correlation ID
- Use log sanitizer to redact PII (email, phone, OTP codes)

Example:
```csharp
_logger.LogInformation(
    "User {UserId} sent message {MessageId} to room {RoomId}",
    userId, messageId, roomId
);
```

### Telemetry
- OpenTelemetry integration for traces, metrics, logs
- Application Insights for Azure monitoring
- Custom metrics: message send rate, active connections, OTP success rate
- Distributed tracing across SignalR, Cosmos DB, Redis

### Health Checks
- `/health` - detailed health check (authenticated)
- `/healthz` - liveness probe (unauthenticated)
- Checks: Cosmos DB, Redis, SignalR Service connectivity

---

## Localization (i18n)

Supported languages (9 total):
- English (en), Polish (pl), German (de), French (fr), Spanish (es)
- Italian (it), Portuguese (pt), Japanese (ja), Chinese (zh)

Resources:
- Server-side: `.resx` files in `Resources/` (e.g., `SharedResources.pl.resx`)
- Client-side: JSON files in `wwwroot/locales/` (e.g., `pl.json`)

After changing `.resx` files, run:
```bash
dotnet build ./src/Chat.sln
# or use task: "clean build (translations)"
```

---

## Common Tasks

### Running Locally
```bash
# In-memory mode (no Azure)
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099

# With Azure resources (requires .env.local)
bash -lc "set -a; source .env.local; dotnet run --project ./src/Chat.Web --urls=https://localhost:5099"
```

### Running Tests
```bash
# All tests
dotnet test src/Chat.sln

# Specific project
dotnet test tests/Chat.IntegrationTests/

# With in-memory mode
Testing__InMemory=true dotnet test src/Chat.sln
```

### Building Frontend Assets
```bash
# Install dependencies
npm install

# Build production bundles (CSS + minified JS)
npm run build:prod
```

### Deploying Infrastructure
```bash
# Via GitHub Actions (manual workflow_dispatch)
# Go to: Actions → Deploy Infrastructure → Run workflow

# Or via Azure CLI
az deployment group create \
  --resource-group rg-signalrchat-dev-weu \
  --template-file infra/bicep/main.bicep \
  --parameters @infra/bicep/main.parameters.dev.bicepparam
```

### Deploying Application Code
```bash
# Via GitHub Actions (manual workflow_dispatch)
# Go to: Actions → CD - Continuous Deployment → Run workflow

# Or push to main branch (auto-deploys to dev)
git push origin main

# Or create release tag (auto-deploys to prod)
git tag v1.0.0
git push origin v1.0.0
```

---

## Azure Resources

### Resource Naming Convention
Format: `<resource-type>-<base-name>-<environment>-<location>`

Examples:
- App Service: `app-signalrchat-dev-weu`
- Cosmos DB: `cosmos-signalrchat-dev-weu`
- Redis: `redis-signalrchat-dev-weu`
- SignalR: `signalr-signalrchat-dev-weu`
- Resource Group: `rg-signalrchat-dev-weu`

### Environments
- **dev**: Development (1 instance, no zone redundancy)
- **staging**: Staging (2 instances, zone redundant)
- **prod**: Production (3 instances, zone redundant)

### Deployment Regions
- Primary: `westeurope` (weu)
- Can expand to multi-region in future

---

## Troubleshooting

### Common Issues

**Issue**: App settings with colon (`:`) fail on Linux
- **Solution**: Use double underscore (`__`) in Bicep: `Cosmos__Database` not `Cosmos:Database`

**Issue**: OTP codes not received
- **Solution**: Check console output in in-memory mode, or verify ACS configuration in Azure

**Issue**: SignalR connection fails
- **Solution**: Check CORS configuration, verify SignalR Service connection string, check browser console

**Issue**: Cosmos DB query slow
- **Solution**: Verify partition key is used in queries, check RU consumption, add indexes

**Issue**: Tests fail with "Cosmos:Database not configured"
- **Solution**: Set `Testing__InMemory=true` environment variable

---

## Additional Resources

- **Documentation**: See `/docs` folder for comprehensive guides
- **Architecture Decisions**: See `/docs/architecture/decisions/` for ADRs
- **API Reference**: See `/docs/reference/api/` for endpoint documentation
- **Contributing**: See [CONTRIBUTING.md](../CONTRIBUTING.md) for contribution guidelines
- **License**: MIT License - see [LICENSE](../LICENSE)

---

## When Generating Code

### Always:
- ✅ Follow existing code style and patterns
- ✅ Add XML documentation comments for public APIs
- ✅ Use dependency injection for services
- ✅ Add tests for new functionality
- ✅ Use structured logging with `ILogger<T>`
- ✅ Handle exceptions gracefully
- ✅ Use `async`/`await` for I/O operations
- ✅ Validate user input
- ✅ Add appropriate security headers

### Never:
- ❌ Hardcode secrets or connection strings
- ❌ Use `Console.WriteLine` (use `ILogger`)
- ❌ Use jQuery (use vanilla JavaScript)
- ❌ Deploy to Windows App Service (use Linux)
- ❌ Use colon (`:`) in Linux app settings (use `__`)
- ❌ Commit secrets to Git
- ❌ Break existing tests without fixing them
- ❌ Skip security considerations

---

**Last Updated**: 2025-11-13  
**Version**: 0.9.5
