# Data Bootstrap Guide

This document explains how to initialize rooms and users for the Chat application in different environments.

## Overview

The Chat application requires initial data setup:
- **Rooms**: Static channels where users communicate (e.g., "general", "ops", "random")
- **Users**: User accounts with fixed room assignments and contact details

Unlike traditional seeding approaches that run automatically on startup, this application uses an **on-demand bootstrap strategy** that gives you full control over data provisioning.

## Why On-Demand Bootstrap?

### Benefits
- ✅ **Explicit control**: Data initialization happens when YOU decide
- ✅ **No accidental overwrites**: Production data is never auto-seeded
- ✅ **Clear audit trail**: Bootstrap is a deliberate, logged operation
- ✅ **Flexibility**: Different strategies for different environments
- ✅ **Testability**: Test fixtures manage their own data independently

### Previous Approach (Removed)
The old `DataSeedHostedService` ran automatically on every startup, which:
- ❌ Created confusion about data provenance
- ❌ Could interfere with production data
- ❌ Made testing more complex (hosted services don't run in test harness)
- ❌ Lacked granular control

---

## Bootstrap Tool

A standalone .NET console application is provided for database seeding:

**Location**: `tools/Chat.DataSeed/`

**Features**:
- Works like ORM migrations - runs on demand, not part of runtime app
- Reuses Chat.Web repositories and models
- Supports Cosmos DB (extensible to other backends)
- Command-line flags: `--clear`, `--dry-run`, `--environment`

### Basic Usage

```bash
# Seed Development environment
cd /Users/michal/Developer/SignalR-Chat
dotnet run --project tools/Chat.DataSeed

# Seed Production environment
dotnet run --project tools/Chat.DataSeed -- --environment Production

# Dry run (preview what would be created)
dotnet run --project tools/Chat.DataSeed -- --dry-run

# Clear existing data first (with confirmation prompt)
dotnet run --project tools/Chat.DataSeed -- --clear
```

### What It Seeds

**Rooms**:
- `general` (id: 1)
- `ops` (id: 2)
- `random` (id: 3)

**Users**:
- `alice`: FixedRooms=["general", "ops"], DefaultRoom="general"
- `bob`: FixedRooms=["general", "random"], DefaultRoom="general"
- `charlie`: FixedRooms=["general"], DefaultRoom="general"

### Configuration

The tool reads configuration from Chat.Web's `appsettings.json`:

```json
{
  "Cosmos": {
    "ConnectionString": "AccountEndpoint=https://...",
    "Database": "chat",
    "UsersContainer": "users",
    "RoomsContainer": "rooms"
  }
}
```

Environment-specific settings (Development/Production) are applied automatically based on `--environment` flag.

---

## Bootstrap Strategies by Environment

### 1. Integration Tests

**Status**: ✅ **Fully automated** (no action needed)

The test fixture (`CustomWebApplicationFactory`) automatically initializes test data:

```csharp
// Test users initialized in CustomWebApplicationFactory.EnsureServerStarted()
- alice: ["general", "ops"]
- bob: ["general", "random"]
- charlie: ["general"]

// Rooms pre-initialized in InMemoryRoomsRepository constructor
- general (id: 1)
- ops (id: 2)
- random (id: 3)
```

**How it works**:
1. `CustomWebApplicationFactory.CreateClient()` calls `EnsureServerStarted()`
2. Test data is initialized via `IUsersRepository.Upsert()`
3. Rooms are pre-created in the in-memory repository constructor
4. All tests share the same fixture data

**Key files**:
- `tests/Chat.IntegrationTests/CustomWebApplicationFactory.cs` (lines 56-120)
- `src/Chat.Web/Repositories/InMemoryRepositories.cs` (lines 30-39)

---

### 2. Local Development

#### Option A: In-Memory Mode (Quickest)

When running with `Testing:InMemory=true` (used by integration tests), no bootstrap needed.

#### Option B: Real Databases (Cosmos DB / Redis)

**Prerequisites**:
- Connection strings configured in `appsettings.Development.json` or `.env.local`
- Cosmos DB database and containers created

**Bootstrap with the .NET Tool**:

```bash
# Run the seeding tool with Development environment
dotnet run --project tools/Chat.DataSeed

# Or explicitly specify environment
dotnet run --project tools/Chat.DataSeed -- --environment Development

# Preview what would be created (dry run)
dotnet run --project tools/Chat.DataSeed -- --dry-run
```

The tool will:
1. Load configuration from `appsettings.Development.json`
2. Connect to Cosmos DB (or fail fast if not configured)
3. Create rooms (general, ops, random) if they don't exist
4. Create users (alice, bob, charlie) with room assignments
5. Skip items that already exist (idempotent)

**Manual Cosmos DB Setup** (alternative to tool):

If you prefer manual setup or need to troubleshoot:
   Use Azure Portal or Azure CLI to insert JSON documents:
   
   ```bash
   # Example using Azure CLI
   az cosmosdb sql database create \
     --account-name <your-account> \
     --name chat \
     --resource-group <your-rg>
   
   az cosmosdb sql container create \
     --account-name <your-account> \
     --database-name chat \
     --name rooms \
     --partition-key-path "/name" \
     --resource-group <your-rg>
   ```

#### Option C: Bootstrap Script (Recommended)

```bash
# From project root
./scripts/bootstrap-data.sh dev
```

**What it does**:
- Checks prerequisites (curl, jq)
- Waits for API to be ready
- Provides instructions for manual data setup
- Can be extended to use admin API endpoints (see Future Enhancements)

---

### 3. Production (Azure)

**Strategy**: Use the .NET seeding tool with production configuration

#### Prerequisites

1. **Cosmos DB** provisioned in Azure
2. **Connection strings** configured via:
   - `appsettings.Production.json`, OR
   - Azure App Service Application Settings, OR
   - Azure Key Vault references, OR
   - Environment variables in deployment pipeline

#### Approach A: Run Seeding Tool Locally (targeting production)

**⚠️ SECURITY WARNING**: Requires production Cosmos DB connection string on your local machine

```bash
# Set production connection string
export Cosmos__ConnectionString="AccountEndpoint=https://your-prod-cosmos.documents.azure.com:443/;AccountKey=..."

# Run with Production environment
dotnet run --project tools/Chat.DataSeed -- --environment Production

# Or use dry-run first to verify
dotnet run --project tools/Chat.DataSeed -- --environment Production --dry-run
```

**Best practices**:
- Use read-write connection string (not primary key)
- Audit the operation
- Run with `--dry-run` first
- Never commit production secrets

#### Approach B: Run in Azure DevOps / GitHub Actions

Add a deployment pipeline step:

```yaml
# Azure Pipelines example
- task: DotNetCoreCLI@2
  displayName: 'Seed Production Data'
  inputs:
    command: 'run'
    projects: 'tools/Chat.DataSeed/Chat.DataSeed.csproj'
    arguments: '-- --environment Production'
  env:
    Cosmos__ConnectionString: $(CosmosConnectionString)  # From pipeline variable
```

```yaml
# GitHub Actions example
- name: Seed Production Data
  run: dotnet run --project tools/Chat.DataSeed -- --environment Production
  env:
    Cosmos__ConnectionString: ${{ secrets.COSMOS_CONNECTION_STRING }}
```

#### Approach C: Run from Azure Container Instance (ACI)

For maximum security, run the seeding tool as a one-time job in Azure:

1. Build container image with Chat.DataSeed
2. Deploy to ACI with Managed Identity
3. Grant ACI Cosmos DB access via RBAC
4. Run container once, then delete

#### Manual Production Bootstrap (alternative)

If the seeding tool is not suitable, manually insert documents via Azure Portal:

**Rooms** (Cosmos `rooms` container):
```json
{
  "id": "1",
  "name": "general",
  "users": []
}
```

**Users** (Cosmos `users` container):
```json
{
  "id": "alice",
  "userName": "alice",
  "fullName": "Alice Johnson",
  "email": "alice@example.com",
  "mobileNumber": "+1234567890",
  "enabled": true,
  "fixedRooms": ["general", "ops"],
  "defaultRoom": "general",
  "avatar": null
}
```

---

## Future Enhancements

### Admin API Endpoints

Consider implementing admin endpoints for runtime data management:

```csharp
// Example: POST /api/admin/users
[Authorize(Policy = "AdminOnly")]
[HttpPost("api/admin/users")]
public async Task<IActionResult> CreateUser([FromBody] ApplicationUser user)
{
    await _usersRepository.UpsertAsync(user);
    return Ok(user);
}

// Example: POST /api/admin/rooms
[Authorize(Policy = "AdminOnly")]
[HttpPost("api/admin/rooms")]
public async Task<IActionResult> CreateRoom([FromBody] Room room)
{
    await _roomsRepository.CreateAsync(room);
    return Ok(room);
}
```

**Benefits**:
- Bootstrap script can use HTTP API instead of direct DB access
- Supports authorization/audit logging
- Works across all storage backends

**Security considerations**:
- Require admin role/claims
- Rate limit admin endpoints
- Log all admin operations
- Validate input thoroughly

### Migration Tool

Create a dedicated .NET console app:

```bash
dotnet new console -n Chat.DataBootstrap
cd Chat.DataBootstrap
dotnet add reference ../src/Chat.Web/Chat.Web.csproj

# Usage:
dotnet run --project Chat.DataBootstrap -- --env prod --data-file bootstrap.json
```

**Advantages**:
- Type-safe access to domain models
- Reuses existing repositories
- Can run as part of CI/CD pipeline
- Supports rollback/idempotency

---

## Quick Reference

| Environment | Method | Command |
|-------------|--------|---------|
| **Integration Tests** | Automatic | Run tests normally |
| **Local Dev (In-Memory)** | Automatic | `dotnet run` with `Testing:InMemory=true` |
| **Local Dev (Real DB)** | Manual SQL/Script | See Option B above |
| **Production** | IaC + Script | `./scripts/bootstrap-data.sh prod` |

---

## Troubleshooting

### "No rooms found"
- **Cause**: Rooms not initialized
- **Fix**: Run bootstrap script or insert rooms manually
- **Verify**: `SELECT * FROM Rooms` or check Cosmos container

### "User not authorized for room"
- **Cause**: User's `FixedRooms` doesn't include target room
- **Fix**: Update user document to include room in `fixedRooms` array
- **Check**: User document in database/Cosmos

### "API not ready" during bootstrap
- **Cause**: Application not running or health endpoint failing
- **Fix**: 
  1. Start application: `dotnet run --project src/Chat.Web`
  2. Check health: `curl http://localhost:5099/health`
  3. Review logs for startup errors

### Test fixture initialization fails
- **Cause**: `CustomWebApplicationFactory.EnsureServerStarted()` unable to create users
- **Fix**: Check that `IUsersRepository` is properly registered in DI
- **Debug**: Add breakpoint in `CustomWebApplicationFactory.cs` line 73

---

## Migration from Old Seeding Approach

If upgrading from a version with `DataSeedHostedService`:

1. **Remove old configuration**:
   - Delete `"Seeding": { "Enabled": false }` from appsettings
   - Remove `DataSeedHostedService` references

2. **Bootstrap existing environments**:
   - Development: Run bootstrap script or manual SQL
   - Production: Use IaC to provision initial data

3. **Update deployment pipelines**:
   - Add bootstrap step after app deployment
   - Ensure idempotency (check if data exists before inserting)

4. **Verify tests**:
   ```bash
   dotnet test src/Chat.sln --filter "FullyQualifiedName~IntegrationTests"
   ```

---

## Best Practices

✅ **DO**:
- Keep bootstrap data in version control (JSON/SQL files)
- Make bootstrap scripts idempotent (check before insert)
- Document required data schema
- Test bootstrap process in staging first
- Log bootstrap operations for audit

❌ **DON'T**:
- Auto-seed production on every startup
- Hard-code production credentials in scripts
- Skip validation of bootstrap data
- Bootstrap in production without backup
- Mix test data with production data

---

## Support

For questions or issues:
1. Check this guide first
2. Review `/docs/ARCHITECTURE.md` for system overview
3. Examine test fixtures in `tests/Chat.IntegrationTests/`
4. Open an issue with reproduction steps

---

**Last updated**: 2025-10-23  
**Related docs**: `ARCHITECTURE.md`, `README.md`
