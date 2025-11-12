# Configuration Guide

Complete reference for configuring SignalR Chat via environment variables and configuration files.

## Configuration Sources

SignalR Chat uses multiple configuration sources in this priority order (highest to lowest):

1. **Environment Variables** (Azure App Service, local .env)
2. **Connection Strings** (Azure App Service Configuration → Connection strings)
3. **appsettings.{Environment}.json** (Development, Production)
4. **appsettings.json** (defaults)
5. **User Secrets** (local development only)

## Environment Variables

### Quick Reference

```bash
# Copy this to .env.local for local development
# DO NOT commit .env.local to source control!

# === Required for Azure Deployment ===
CUSTOMCONNSTR_Cosmos=AccountEndpoint=https://...;AccountKey=...
CUSTOMCONNSTR_Redis=your-redis.redis.cache.windows.net:6380,password=...,ssl=True
Otp__Pepper=YOUR_BASE64_PEPPER_HERE_44_CHARS

# === Optional Azure Services ===
CUSTOMCONNSTR_AzureSignalR=Endpoint=https://...;AccessKey=...
CUSTOMCONNSTR_AzureCommunicationServices=endpoint=https://...;accesskey=...
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=https://...

# === OTP Configuration ===
Otp__OtpTtlSeconds=300
Otp__MaxAttempts=5
Otp__MemCost=65536
Otp__TimeCost=4
Otp__Parallelism=4

# === Notification Configuration (if using ACS) ===
Communication__EmailSender=noreply@your-domain.com
Communication__SmsSender=+1234567890
Notifications__UnreadDelaySeconds=60

# === Cosmos DB Configuration ===
Cosmos__DatabaseName=chat
Cosmos__MessagesTtlSeconds=2592000

# === Application Insights (Production) ===
Logging__ApplicationInsights__LogLevel__Default=Information
```

## Detailed Configuration

### Connection Strings

#### Cosmos DB

**Environment Variable**: `CUSTOMCONNSTR_Cosmos`

```bash
# Azure Cosmos DB connection string format
AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=your-key==
```

**Where to find**:
- Azure Portal → Cosmos DB → Keys → Primary Connection String

**Local development**:
- Use Azure Cosmos DB Emulator: `AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv...`

#### Redis

**Environment Variable**: `CUSTOMCONNSTR_Redis`

```bash
# Azure Redis connection string format
your-redis.redis.cache.windows.net:6380,password=your-key==,ssl=True,abortConnect=False
```

**Where to find**:
- Azure Portal → Redis → Access keys → Primary connection string (StackExchange.Redis)

**Local development**:
- Docker Redis: `localhost:6379`
- No password needed for local

#### Azure SignalR Service (Optional)

**Environment Variable**: `CUSTOMCONNSTR_AzureSignalR`

```bash
# Azure SignalR Service connection string
Endpoint=https://your-signalr.service.signalr.net;AccessKey=your-key==;Version=1.0;
```

**When to use**:
- ✅ Scaling beyond 1000 concurrent connections
- ✅ Multi-region deployment
- ❌ Single-instance development

#### Azure Communication Services (Optional)

**Environment Variable**: `CUSTOMCONNSTR_AzureCommunicationServices`

```bash
# ACS connection string
endpoint=https://your-acs.communication.azure.com/;accesskey=your-key==
```

**When to use**:
- ✅ Email notifications for unread messages
- ✅ SMS notifications for OTP codes
- ❌ Not required for basic functionality

### OTP Configuration

#### Pepper (Required for Production)

**Environment Variable**: `Otp__Pepper`

**Purpose**: Secret key for OTP hashing (defense in depth)

**Generate**:
```bash
# Generate 32-byte Base64 pepper (44 characters)
openssl rand -base64 32
```

**Example**: `dGhpcyBpcyBhIHNlY3JldCBwZXBwZXIga2V5IGZvciBPVFA=`

**Storage**:
- ✅ Azure App Service → Configuration → Application Settings
- ✅ Azure Key Vault (recommended for production)
- ❌ Never commit to source control

**Rotation**: See [Authentication Guide](../features/authentication.md#pepper-rotation)

#### OTP Time-to-Live

**Environment Variable**: `Otp__OtpTtlSeconds`  
**Default**: `300` (5 minutes)  
**Range**: `60` - `600` seconds

```bash
# Allow 10 minutes for OTP codes
Otp__OtpTtlSeconds=600
```

#### OTP Maximum Attempts

**Environment Variable**: `Otp__MaxAttempts`  
**Default**: `5`  
**Range**: `3` - `10`

```bash
# Allow 3 failed attempts before lockout
Otp__MaxAttempts=3
```

**Behavior**: After N failed attempts, user is locked out for the OTP TTL duration.

#### Argon2id Parameters

**Memory Cost** (`Otp__MemCost`):
- Default: `65536` (64 MB)
- Range: `32768` - `131072` (32 MB - 128 MB)

**Time Cost** (`Otp__TimeCost`):
- Default: `4` iterations
- Range: `2` - `8`

**Parallelism** (`Otp__Parallelism`):
- Default: `4` threads
- Range: `1` - `8`

**Example**:
```bash
# Increase security (slower hashing)
Otp__MemCost=131072
Otp__TimeCost=6
Otp__Parallelism=4
```

### Cosmos DB Configuration

#### Database Name

**Environment Variable**: `Cosmos__DatabaseName`  
**Default**: `chat`

```bash
# Use different database name
Cosmos__DatabaseName=signalrchat
```

#### Message TTL (Time-to-Live)

**Environment Variable**: `Cosmos__MessagesTtlSeconds`  
**Default**: `null` (disabled)

```bash
# Delete messages after 30 days
Cosmos__MessagesTtlSeconds=2592000

# Enable TTL but never expire messages
Cosmos__MessagesTtlSeconds=-1

# Disable TTL (explicit)
Cosmos__MessagesTtlSeconds=
```

**Options**:
- `null` / empty: TTL disabled
- `-1`: TTL enabled, messages never expire
- `> 0`: Messages expire after N seconds

### Notification Configuration

#### Email Sender

**Environment Variable**: `Communication__EmailSender`  
**Required if**: Using Azure Communication Services for email

```bash
Communication__EmailSender=noreply@your-domain.com
```

**Requirements**:
- Verified sender in ACS Email Communication Service
- Domain must be configured in ACS

#### SMS Sender

**Environment Variable**: `Communication__SmsSender`  
**Required if**: Using Azure Communication Services for SMS

```bash
Communication__SmsSender=+1234567890
```

**Requirements**:
- Phone number provisioned in ACS
- SMS capability enabled

#### Unread Notification Delay

**Environment Variable**: `Notifications__UnreadDelaySeconds`  
**Default**: `60` seconds

```bash
# Send notifications after 2 minutes of inactivity
Notifications__UnreadDelaySeconds=120
```

**Purpose**: Delay before sending email/SMS for unread messages (prevents spam during active conversations)

### Application Insights

#### Connection String

**Environment Variable**: `APPLICATIONINSIGHTS_CONNECTION_STRING`

```bash
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=https://...
```

**Where to find**:
- Azure Portal → Application Insights → Overview → Connection String

#### Logging Level

**Environment Variable**: `Logging__ApplicationInsights__LogLevel__Default`  
**Default**: `Warning` (Production), `Debug` (Development)

```bash
# Enable Information-level logs in Production
Logging__ApplicationInsights__LogLevel__Default=Information
```

## Configuration by Environment

### Development (In-Memory)

Minimal configuration for local testing:

```bash
# No environment variables needed!
# Uses in-memory storage for everything
```

### Development (with Azure)

`.env.local`:
```bash
CUSTOMCONNSTR_Cosmos=AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv...
CUSTOMCONNSTR_Redis=localhost:6379
Otp__Pepper=dev-pepper-not-for-production
```

### Staging

Azure App Service → Configuration:
```bash
CUSTOMCONNSTR_Cosmos=[from Azure Portal]
CUSTOMCONNSTR_Redis=[from Azure Portal]
CUSTOMCONNSTR_AzureSignalR=[from Azure Portal]
Otp__Pepper=[from Key Vault]
APPLICATIONINSIGHTS_CONNECTION_STRING=[from Azure Portal]
Cosmos__MessagesTtlSeconds=2592000
```

### Production

Same as staging + additional monitoring:
```bash
Logging__ApplicationInsights__LogLevel__Default=Information
Notifications__UnreadDelaySeconds=60
Otp__MaxAttempts=5
```

## Configuration Files

### appsettings.json

Default configuration (committed to source control):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Otp": {
    "OtpTtlSeconds": 300,
    "MaxAttempts": 5,
    "MemCost": 65536,
    "TimeCost": 4,
    "Parallelism": 4
  },
  "Cosmos": {
    "DatabaseName": "chat"
  },
  "Notifications": {
    "UnreadDelaySeconds": 60
  }
}
```

### appsettings.Development.json

Development overrides:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

### appsettings.Production.json

Production overrides:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Chat.Web": "Information"
    }
  }
}
```

## Azure App Service Configuration

### Where to Set

Azure Portal → App Service → Configuration:

1. **Connection strings** (preferred for connection strings):
   - Type: Custom
   - Name: `Cosmos`, `Redis`, `AzureSignalR`, `AzureCommunicationServices`
   - Value: Connection string
   - ✅ Available as `CUSTOMCONNSTR_{Name}`

2. **Application settings** (for other config):
   - Name: `Otp__Pepper`
   - Value: Base64 pepper string
   - ✅ Available as environment variable

### Deployment Slots

Configuration can be:
- **Slot-specific** (different per environment)
- **Shared** (same across all slots)

**Recommended slot-specific**:
- Connection strings
- `Otp__Pepper`
- `APPLICATIONINSIGHTS_CONNECTION_STRING`

**Recommended shared**:
- `Otp__OtpTtlSeconds`
- `Notifications__UnreadDelaySeconds`

## Validation

### Configuration Guards

SignalR Chat validates configuration at startup in `ConfigurationGuards.cs`:

✅ **Required checks**:
- Cosmos connection string present
- Redis connection string present (if not in-memory mode)
- OTP pepper present (if not in-memory/development mode)

❌ **Startup fails if**:
- Missing required connection strings
- Invalid connection string format
- Missing pepper in production

### Testing Configuration

```bash
# Test connection strings locally
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099

# Check logs for validation errors
# Look for: "Configuration validation failed"
```

## Troubleshooting

### "Configuration validation failed"

**Cause**: Missing required environment variable

**Fix**: Set required connection strings and pepper

### "Access to Cosmos DB denied"

**Cause**: Invalid connection string or firewall rules

**Fix**: 
1. Verify connection string in Azure Portal
2. Check Cosmos DB → Networking → Allow access from Azure services
3. Add your IP to firewall rules (development)

### "Redis connection failed"

**Cause**: Invalid connection string or network access

**Fix**:
1. Verify connection string includes `,ssl=True` for Azure
2. Check Redis → Networking → Private endpoint or public access
3. Verify NSG rules allow traffic

## See Also

- [Installation Guide](installation.md) - Set up Azure resources
- [Authentication Guide](../features/authentication.md) - OTP and pepper details
- [Production Checklist](../deployment/production-checklist.md) - Pre-deployment config
- [Troubleshooting](../deployment/troubleshooting.md) - Common issues

---

**Next**: [Installation Guide](installation.md) | [Back to Getting Started](README.md)
