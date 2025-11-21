# Local Development Setup

This guide covers setting up a complete development environment for SignalR Chat, including tooling, debugging, and hot reload capabilities.

## Prerequisites

### Required

- **[.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** - Build and runtime environment
- **Git** - Version control
- **Visual Studio Code** or **Visual Studio 2022** - IDE

### Recommended

- **[Node.js 18+](https://nodejs.org/)** - For frontend build tools (esbuild, sass)
- **[Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)** - For Azure resource management
- **[Redis](https://redis.io/docs/getting-started/)** - For local OTP storage and rate limiting (optional)

### Optional

- **Docker Desktop** - For running Redis, Cosmos DB Emulator
- **[Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/emulator)** - For local database testing
- **Postman** or **REST Client** - For API testing

## Development Modes

SignalR Chat supports two development modes:

### 1. In-Memory Mode (Default)
- ✅ **No Azure dependencies** - Works completely offline
- ✅ **Fast startup** - No connection setup needed
- ✅ **Perfect for UI development** - All features work
- ❌ **No persistence** - Data lost on restart
- ❌ **Single instance only** - Can't test load balancing

### 2. Azure Mode (Full Feature Set)
- ✅ **Full persistence** - Cosmos DB stores messages
- ✅ **Multi-instance** - Test with Azure SignalR Service
- ✅ **Production-like** - Same configuration as deployment
- ❌ **Requires Azure resources** - See [Installation Guide](../getting-started/installation.md)
- ❌ **Connection strings needed** - Stored in `.env.local`

## Quick Setup (In-Memory Mode)

### 1. Clone and Build

```bash
# Clone the repository
git clone https://github.com/smereczynski/SignalR-Chat.git
cd SignalR-Chat

# Build the solution
dotnet build ./src/Chat.sln
```

### 2. Run the Application

```bash
# Run with default settings (in-memory mode)
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

### 3. Verify It Works

1. Open http://localhost:5099
2. Login as **alice** (check terminal for OTP code)
3. Join **General** room
4. Send a message

**You're ready to develop!** 🎉

## Full Setup (Azure Mode)

### 1. Set Up Azure Resources

Follow the [Installation Guide](../getting-started/installation.md) to create:
- Azure Cosmos DB account
- Azure Cache for Redis
- Azure SignalR Service
- Azure Communication Services (optional, for email OTP)

### 2. Configure Connection Strings

Create `.env.local` in the repository root:

```bash
# Azure Cosmos DB
COSMOS_CONNECTION_STRING="AccountEndpoint=https://YOUR_ACCOUNT.documents.azure.com:443/;AccountKey=YOUR_KEY=="

# Azure Cache for Redis
REDIS_CONNECTION_STRING="YOUR_REDIS.redis.cache.windows.net:6380,password=YOUR_PASSWORD,ssl=True,abortConnect=False"

# Azure SignalR Service
SIGNALR_CONNECTION_STRING="Endpoint=https://YOUR_SIGNALR.service.signalr.net;AccessKey=YOUR_KEY;Version=1.0;"

# Azure Communication Services (optional)
ACS_CONNECTION_STRING="endpoint=https://YOUR_ACS.communication.azure.com/;accesskey=YOUR_KEY"
ACS_EMAIL_FROM="DoNotReply@YOUR_DOMAIN.azurecomm.net"

# Application Insights (optional)
APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=YOUR_KEY;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/"
```

**⚠️ Security**: Never commit `.env.local` to Git (already in `.gitignore`)

### 3. Run with Azure Resources

```bash
# Load environment variables from .env.local
bash -lc "set -a; source .env.local; dotnet run --project ./src/Chat.Web --urls=https://localhost:5099"
```

Or use the VS Code task: **"Run Chat (Azure local env)"**

## IDE Setup

### Visual Studio Code (Recommended)

#### 1. Install Extensions

Required:
- [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)
- [C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp)

Recommended:
- [Azure Tools](https://marketplace.visualstudio.com/items?itemName=ms-vscode.vscode-node-azure-pack)
- [REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client)
- [EditorConfig](https://marketplace.visualstudio.com/items?itemName=EditorConfig.EditorConfig)

#### 2. Open the Workspace

```bash
code .
```

VS Code will detect the `.sln` file and configure IntelliSense automatically.

#### 3. Configure Tasks

The repository includes pre-configured tasks in `.vscode/tasks.json`:

- **Build All** (`Cmd+Shift+B`) - Frontend + backend build
- **Run Chat (Azure local env)** - Run with `.env.local`
- **Test** - Run all tests
- **Clean Build (translations)** - Rebuild after `.resx` changes

#### 4. Configure Debugging

Create or update `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch Chat.Web (in-memory)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "dotnet build",
      "program": "${workspaceFolder}/src/Chat.Web/bin/Debug/net9.0/Chat.Web.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/Chat.Web",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:5099"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      }
    },
    {
      "name": "Launch Chat.Web (Azure)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "dotnet build",
      "program": "${workspaceFolder}/src/Chat.Web/bin/Debug/net9.0/Chat.Web.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/Chat.Web",
      "stopAtEntry": false,
      "envFile": "${workspaceFolder}/.env.local",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "https://localhost:5099"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      }
    }
  ]
}
```

**Debugging**:
- Press `F5` to start debugging
- Set breakpoints by clicking left of line numbers
- Use Debug Console to evaluate expressions

### Visual Studio 2022

#### 1. Open the Solution

```bash
# Open in Visual Studio
start src/Chat.sln
```

#### 2. Configure User Secrets (Azure Mode)

Right-click `Chat.Web` project → **Manage User Secrets**:

```json
{
  "ConnectionStrings:Cosmos": "YOUR_COSMOS_CONNECTION_STRING",
  "ConnectionStrings:Redis": "YOUR_REDIS_CONNECTION_STRING",
  "ConnectionStrings:SignalR": "YOUR_SIGNALR_CONNECTION_STRING",
  "ConnectionStrings:Acs": "YOUR_ACS_CONNECTION_STRING",
  "Acs:EmailFrom": "DoNotReply@YOUR_DOMAIN.azurecomm.net"
}
```

#### 3. Set Startup Project

Right-click `Chat.Web` → **Set as Startup Project**

#### 4. Run and Debug

- Press `F5` to start with debugging
- Press `Ctrl+F5` to start without debugging

## Frontend Development

### 1. Install Dependencies

```bash
npm install
```

This installs:
- `esbuild` - Fast JavaScript bundler
- `sass` - CSS preprocessor
- `npm-run-all` - Run multiple npm scripts

### 2. Build Frontend Assets

```bash
# Development build (unminified, with source maps)
npm run build:dev

# Production build (minified, no source maps)
npm run build:prod

# Watch mode (rebuild on file changes)
npm run watch
```

### 3. Frontend File Structure

```
src/Chat.Web/wwwroot/
├── css/
│   └── site.scss           # Main stylesheet (compiled to site.css)
├── js/
│   ├── chat.js             # Chat room functionality
│   ├── login.js            # Login/OTP flow
│   └── common.js           # Shared utilities
├── lib/                    # Third-party libraries (Bootstrap, SignalR)
└── locales/                # i18n JSON files (9 languages)
```

### 4. Hot Reload

ASP.NET Core supports hot reload for:
- ✅ Razor Pages (`.cshtml`)
- ✅ CSS (`.scss` after rebuild)
- ❌ C# code (requires restart)
- ❌ JavaScript (requires page refresh)

**Workflow**:
1. Run `npm run watch` in one terminal (auto-rebuild JS/CSS)
2. Run `dotnet watch` in another terminal (auto-restart on C# changes)
3. Edit files and refresh browser

```bash
# Terminal 1: Watch frontend
npm run watch

# Terminal 2: Watch backend
dotnet watch run --project ./src/Chat.Web --urls=http://localhost:5099
```

## Running Tests

### All Tests

```bash
# Run all tests (unit + integration + web)
dotnet test src/Chat.sln

# Run with detailed output
dotnet test src/Chat.sln --logger "console;verbosity=detailed"
```

### Specific Test Projects

```bash
# Unit tests only (utilities, services)
dotnet test tests/Chat.Tests/

# Integration tests (SignalR hubs, auth flows)
dotnet test tests/Chat.IntegrationTests/

# Web tests (health checks, security headers)
dotnet test tests/Chat.Web.Tests/
```

### Test Modes

#### In-Memory Mode (Default)
```bash
dotnet test src/Chat.sln
```
- Uses in-memory database and OTP storage
- Fast and isolated
- Some SignalR tests may fail (see [Testing Guide](testing.md))

#### Azure Mode (Full Feature Set)
```bash
# Load .env.local for tests
bash -lc "set -a; source .env.local; dotnet test src/Chat.sln"
```
- Uses Azure resources
- Tests full integration
- All tests should pass

### Known Test Issues

⚠️ **SignalR Integration Tests**: 11-14 tests may fail locally without Azure SignalR Service. This is expected behavior. See [Testing Guide](testing.md#known-issues) and [Issue #113](https://github.com/smereczynski/SignalR-Chat/issues/113) for details.

## Configuration

### appsettings.json Hierarchy

ASP.NET Core loads configuration in this order (later overrides earlier):

1. `appsettings.json` (base settings)
2. `appsettings.{Environment}.json` (e.g., `Development`, `Production`)
3. User secrets (`dotnet user-secrets`)
4. Environment variables (`.env.local` via `bash -lc`)
5. Command-line arguments

### Key Configuration Sections

```json
{
  "Cosmos": {
    "Database": "chat",
    "Containers": {
      "Messages": "messages",
      "Rooms": "rooms",
      "Users": "users"
    }
  },
  "Redis": {
    "Database": 0
  },
  "Otp": {
    "OtpTtlSeconds": 300,
    "OtpLength": 6,
    "MaxAttempts": 5,
    "AttemptWindowMinutes": 15
  },
  "RateLimiting": {
    "MessageSend": {
      "PermitLimit": 10,
      "WindowSeconds": 60
    }
  }
}
```

### Environment Variables

```bash
# Core settings
ASPNETCORE_ENVIRONMENT=Development          # Development, Staging, Production
ASPNETCORE_URLS=https://localhost:5099      # Listening URLs

# Azure connection strings (override appsettings)
COSMOS_CONNECTION_STRING="..."              # Cosmos DB
REDIS_CONNECTION_STRING="..."               # Redis
SIGNALR_CONNECTION_STRING="..."             # SignalR Service
ACS_CONNECTION_STRING="..."                 # Communication Services

# Testing mode
Testing__InMemory=true                      # Force in-memory mode (no Azure)

# Observability
APPLICATIONINSIGHTS_CONNECTION_STRING="..." # App Insights
```

## Local Redis Setup (Optional)

### macOS (Homebrew)

```bash
# Install Redis
brew install redis

# Start Redis
brew services start redis

# Verify
redis-cli ping
# Should return: PONG
```

### Windows (Chocolatey)

```bash
# Install Redis
choco install redis-64

# Start Redis
redis-server

# Verify (in another terminal)
redis-cli ping
```

### Docker

```bash
# Run Redis container
docker run -d --name redis -p 6379:6379 redis:7-alpine

# Verify
docker exec redis redis-cli ping
```

### Configure Application

Update `.env.local`:

```bash
REDIS_CONNECTION_STRING="localhost:6379,abortConnect=False"
```

## Local Cosmos DB Emulator (Optional)

### Windows Only

1. Download [Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/emulator)
2. Install and start the emulator
3. Navigate to https://localhost:8081/_explorer/ to verify

### Docker (Cross-Platform)

```bash
# Run Cosmos DB emulator (Linux container)
docker run -d --name cosmosdb \
  -p 8081:8081 -p 10251:10251 -p 10252:10252 -p 10253:10253 -p 10254:10254 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest

# Verify (wait 30 seconds for startup)
curl -k https://localhost:8081/_explorer/
```

### Configure Application

Update `.env.local`:

```bash
COSMOS_CONNECTION_STRING="AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
```

**⚠️ Note**: The emulator key is publicly known and safe to commit.

## Common Development Tasks

### Add a New Localization Language

1. Duplicate `.resx` file:
   ```bash
   cp src/Chat.Web/Resources/SharedResources.en.resx \
      src/Chat.Web/Resources/SharedResources.xx.resx
   ```

2. Translate strings in new `.resx` file

3. Add locale JSON:
   ```bash
   cp src/Chat.Web/wwwroot/locales/en.json \
      src/Chat.Web/wwwroot/locales/xx.json
   ```

4. Translate JSON strings

5. Rebuild (required for `.resx` changes):
   ```bash
   rm -rf src/Chat.Web/bin src/Chat.Web/obj
   dotnet build ./src/Chat.sln
   ```

### Add a New SignalR Hub Method

1. Add method to `ChatHub.cs`:
   ```csharp
   public async Task MyNewMethod(string param)
   {
       await Clients.All.SendAsync("ReceiveMyEvent", param);
   }
   ```

2. Add client-side handler in `chat.js`:
   ```javascript
   connection.on("ReceiveMyEvent", (param) => {
       console.log("Received:", param);
   });
   ```

3. Test with hot reload (`dotnet watch`)

### Update Database Schema

1. Modify model in `src/Chat.Web/Models/`
2. Update repository in `src/Chat.Web/Repositories/`
3. Add migration logic if needed
4. Test with both in-memory and Cosmos DB

### Add a New REST Endpoint

1. Create/update controller in `src/Chat.Web/Controllers/`
2. Add route and action:
   ```csharp
   [HttpGet("api/my-endpoint")]
   public IActionResult MyEndpoint()
   {
       return Ok(new { message = "Hello" });
   }
   ```

3. Add authorization if needed: `[Authorize]`
4. Add integration tests

## Troubleshooting

### Issue: Build Fails After Translation Changes

**Solution**: Clean and rebuild:
```bash
rm -rf src/Chat.Web/bin src/Chat.Web/obj
dotnet build ./src/Chat.sln
```

### Issue: Port 5099 Already in Use

**Solution**: Change port:
```bash
dotnet run --project ./src/Chat.Web --urls=http://localhost:5100
```

### Issue: SignalR Connection Fails

**Possible causes**:
1. **CORS issue** - Check browser console for CORS errors
2. **SignalR Service down** - Check Azure portal
3. **Connection string wrong** - Verify `.env.local`

**Debug steps**:
```bash
# Test health endpoint
curl http://localhost:5099/health

# Check SignalR negotiate endpoint
curl -i http://localhost:5099/chathub/negotiate
```

### Issue: Tests Fail with "Cosmos:Database not configured"

**Solution**: Run tests in in-memory mode:
```bash
Testing__InMemory=true dotnet test src/Chat.sln
```

### Issue: OTP Codes Not Showing

**In-memory mode**: Check terminal output where you ran `dotnet run`

**Azure mode**: Check Azure Communication Services email or Redis logs

### Issue: Hot Reload Not Working

**C# changes**: Use `dotnet watch` instead of `dotnet run`

**JavaScript changes**: Run `npm run watch` and refresh browser

**CSS changes**: Rebuild with `npm run build:dev`

## Performance Tips

### Faster Builds

```bash
# Build in parallel (use all CPU cores)
dotnet build -m

# Skip tests during build
dotnet build --no-restore

# Incremental build (only changed files)
dotnet build --no-dependencies
```

### Faster Tests

```bash
# Run tests in parallel
dotnet test --parallel

# Run specific test category
dotnet test --filter "Category=Unit"

# Skip slow integration tests
dotnet test --filter "FullyQualifiedName!~Integration"
```

### Faster Startup

```bash
# Skip HTTPS redirection
dotnet run --no-https

# Use in-memory mode (no connection setup)
Testing__InMemory=true dotnet run
```

## Next Steps

- **[Testing Guide](testing.md)** - Learn about test structure and best practices
- **[Contributing Guide](../../CONTRIBUTING.md)** - Contribution workflow
- **[Architecture Overview](../architecture/overview.md)** - System design
- **[Deployment Guide](../deployment/README.md)** - Deploy to Azure

## Getting Help

- **Questions**: Open a [GitHub Discussion](https://github.com/smereczynski/SignalR-Chat/discussions)
- **Bugs**: Open a [GitHub Issue](https://github.com/smereczynski/SignalR-Chat/issues)
- **Documentation**: See [docs/README.md](../README.md)

---

**Happy coding!** 🚀
