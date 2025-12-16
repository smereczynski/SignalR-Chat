# Configuration Guide

Complete reference for configuring SignalR Chat via environment variables and configuration files.

## Configuration Sources

SignalR Chat uses multiple configuration sources in this priority order (highest to lowest):

1. **Environment Variables** (Azure App Service, local .env)
2. **Connection Strings** (Azure App Service Configuration → Connection strings)
3. **appsettings.{Environment}.json** (Development, Staging, Production)
4. **User Secrets** (local development only)


## GitHub Actions Variables & Secrets

For Azure deployment via GitHub Actions, you must configure both environment variables and secrets in your repository:

- [GitHub Variables Guide](../deployment/github-variables.md)
- [GitHub Secrets Guide](../deployment/github-secrets.md)

These are required for Bicep infrastructure deployment and application configuration. See the guides for full lists and examples.

---

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
Cosmos__Database=chat
Cosmos__MessagesTtlSeconds=2592000

# === CORS Configuration (Production) ===
Cors__AllowedOrigins__0=https://signalrchat-prod-plc.azurewebsites.net
Cors__AllowAllOrigins=false

# === Rate Limiting ===
RateLimiting__MarkRead__MarkReadPermitLimit=100
RateLimiting__MarkRead__MarkReadWindowSeconds=10

# === Entra ID Automatic SSO ===
EntraId__AutomaticSso__Enable=true
EntraId__AutomaticSso__AttemptOncePerSession=true

# === Host Filtering (Production) ===
AllowedHosts=signalrchat-prod-plc.azurewebsites.net

# === Logging ===
Serilog__WriteToFile=false
Serilog__WriteToConsole=false
Logging__LogLevel__Default=Warning
```

Note: In Azure deployments, `Serilog__WriteToConsole` is set to `false` by the infrastructure template to reduce `AppServiceConsoleLogs` volume. Set it to `true` temporarily when you explicitly want more console output.

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

#### OTP Hashing

**Environment Variable**: `Otp__HashingEnabled`  
**Default**: `true`

```bash
# Disable hashing (DEVELOPMENT ONLY - not recommended)
Otp__HashingEnabled=false
```

**Purpose**: Controls whether OTP codes are hashed with Argon2id before storage.

**Behavior**:
- `true` (default): Codes hashed with Argon2id using pepper + salt (secure)
- `false`: Codes stored in plaintext (insecure, for debugging only)

⚠️ **Security Warning**: Never disable hashing in production environments.

### CORS Configuration

**Purpose**: Controls which origins can access the SignalR endpoints and API.

#### Allowed Origins

**Environment Variable**: `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc.  
**appsettings.{Environment}.json**: `Cors:AllowedOrigins` array

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://signalrchat-prod-plc.azurewebsites.net",
      "https://app.contoso.com"
    ]
  }
}
```

**Environment variable format**:
```bash
Cors__AllowedOrigins__0=https://signalrchat-prod-plc.azurewebsites.net
Cors__AllowedOrigins__1=https://app.contoso.com
```

#### Allow All Origins (Development Only)

**Environment Variable**: `Cors__AllowAllOrigins`  
**Default**: `false` (Production), `true` (Development)

```json
{
  "Cors": {
    "AllowAllOrigins": true
  }
}
```

⚠️ **Security Warning**: 
- `AllowAllOrigins: true` should **ONLY** be used in Development
- **NEVER** enable in Production - exposes your API to CSRF attacks
- Use explicit `AllowedOrigins` list instead

**Behavior**:
- `true`: Accepts requests from any origin (development convenience)
- `false`: Only accepts requests from origins in `AllowedOrigins` list

### Rate Limiting Configuration

**Purpose**: Protects against abuse by limiting request frequency.

#### Mark Read Rate Limiting

**Environment Variables**:
- `RateLimiting__MarkRead__MarkReadPermitLimit`
- `RateLimiting__MarkRead__MarkReadWindowSeconds`

**Defaults**:
- Permit Limit: `100` requests
- Window: `10` seconds

```bash
# Allow 50 mark-read requests per 5 seconds
RateLimiting__MarkRead__MarkReadPermitLimit=50
RateLimiting__MarkRead__MarkReadWindowSeconds=5
```

**appsettings.{Environment}.json**:
```json
{
  "RateLimiting": {
    "MarkRead": {
      "MarkReadPermitLimit": 100,
      "MarkReadWindowSeconds": 10
    }
  }
}
```

**Behavior**:
- Limits `/api/messages/{id}/read` endpoint calls per user
- After exceeding limit: HTTP 429 (Too Many Requests)
- Counter resets after window expires
- Prevents flooding with read receipt updates

### Entra ID Configuration

See [Authentication Guide](../features/authentication.md) for complete Entra ID setup.

#### Automatic Silent SSO

**Purpose**: Attempts seamless authentication using existing Microsoft session.

**Environment Variables**:
- `EntraId__AutomaticSso__Enable`
- `EntraId__AutomaticSso__AttemptOncePerSession`
- `EntraId__AutomaticSso__AttemptCookieName`

**Defaults**:
- Enable: `true`
- AttemptOncePerSession: `true`
- AttemptCookieName: `sso_attempted_v2`

```bash
# Enable automatic silent SSO
EntraId__AutomaticSso__Enable=true
EntraId__AutomaticSso__AttemptOncePerSession=true
EntraId__AutomaticSso__AttemptCookieName=sso_attempted_v2
```

**appsettings.{Environment}.json**:
```json
{
  "EntraId": {
    "AutomaticSso": {
      "Enable": true,
      "AttemptOncePerSession": true,
      "AttemptCookieName": "sso_attempted_v2"
    }
  }
}
```

**Behavior**:
- When enabled: First visit to `/` or `/chat` triggers silent authentication attempt
- Uses OIDC `prompt=none` to avoid user interaction
- If successful: User logged in seamlessly
- If failed: Redirected to `/login?reason=sso_failed`
- Cookie prevents repeated attempts (10-minute expiration)

**When to disable**:
- ❌ Pure OTP-only deployments (no Entra ID)
- ❌ Debugging authentication flows
- ✅ Keep enabled for hybrid Entra ID + OTP setups

### Host Filtering Configuration

**Purpose**: Protects against host header injection attacks.

#### Allowed Hosts

**Environment Variable**: `AllowedHosts`  
**Default**: `*` (all hosts)

```bash
# Production: Specify exact hostnames
AllowedHosts=signalrchat-prod-plc.azurewebsites.net;app.contoso.com

# Development: Allow all (convenient but less secure)
AllowedHosts=*
```

**appsettings.{Environment}.json**:
```json
{
  "AllowedHosts": "signalrchat-prod-plc.azurewebsites.net;app.contoso.com"
}
```

**Format**: Semicolon-separated list of allowed hostnames

**Behavior**:
- `*`: Accepts requests with any `Host` header (development/staging)
- Specific hostnames: Only accepts requests matching listed hosts
- Invalid host: HTTP 400 (Bad Request)

**Recommendations**:
- ✅ Development: `*` (convenience)
- ✅ Production: Explicit hostname list (security)
- ✅ Include all expected hostnames (app domain, CDN, reverse proxy)

### Cosmos DB Configuration

#### Database Name

**Environment Variable**: `Cosmos__Database`  
**Default**: `chat`

```bash
# Use different database name
Cosmos__Database=signalrchat
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

### Logging Configuration

#### Console (stdout/stderr)

**Environment Variable**: `Serilog__WriteToConsole`  
**Default**: `false` for Azure deployments (set by Bicep)

Controls whether Serilog writes non-error logs to console (stdout). Error-level logs are always written to stderr by default.

In Azure App Service on Linux, stdout/stderr is typically forwarded to Log Analytics as **AppServiceConsoleLogs**, so leaving this `false` reduces duplication (especially if you already export logs to Application Insights / Azure Monitor).

```bash
# Disable console output (recommended for Staging/Production)
Serilog__WriteToConsole=false

# Enable console output temporarily for troubleshooting
Serilog__WriteToConsole=true
```

#### File Logging (Optional)

**Environment Variable**: `Serilog__WriteToFile`  
**Default**: `false` (disabled)

```bash
# Enable file logging for troubleshooting
Serilog__WriteToFile=true
```

**Behavior**:
- When enabled: Logs written to `logs/chat-YYYYMMDD.log`
- Rolling interval: Daily
- Retention: 7 days (automatic cleanup)
- Log format: `[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}`

**When to use**:
- ✅ Local troubleshooting and debugging
- ✅ Capturing detailed logs without cloud costs
- ✅ Diagnosing issues during development
- ❌ Production (use Application Insights instead)

**Performance impact**: Minimal when disabled (default). Small disk I/O overhead when enabled.

### Application Insights

#### Connection String

**Environment Variable**: `APPLICATIONINSIGHTS_CONNECTION_STRING`

```bash
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=https://...
```

**Where to find**:
- Azure Portal → Application Insights → Overview → Connection String

#### Logging Level

**Environment Variable**: `Logging__LogLevel__Default`  
**Defaults**: Driven by `appsettings.{Environment}.json` (see below)

```bash
# Example: temporarily enable Information-level logs
Logging__LogLevel__Default=Information

# Example: keep noisy dependencies at Warning
Logging__LogLevel__Microsoft=Warning
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
# Production defaults to Warning-level logging via appsettings.Production.json.
# You can temporarily override it via env vars if needed:
Logging__LogLevel__Default=Information
Notifications__UnreadDelaySeconds=60
Otp__MaxAttempts=5
```

## Configuration Files

This repo uses environment-specific files under `src/Chat.Web/`:
- `appsettings.Development.json`
- `appsettings.Staging.json`
- `appsettings.Production.json`

### appsettings.Development.json

Development overrides:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information",
      "Chat.Web": "Debug"
    }
  },
  "ApplicationInsights": {
    "SamplingSettings": {
      "IsEnabled": true,
      "MaxTelemetryItemsPerSecond": 10,
      "EnableAdaptiveSampling": true
    }
  }
}
```

Notes:
- Development is intentionally verbose.
- Application Insights sampling is enabled in Development.

### appsettings.Staging.json

Staging overrides:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Chat.Web": "Information"
    }
  },
  "ApplicationInsights": {
    "SamplingSettings": {
      "IsEnabled": false
    }
  }
}
```

Notes:
- Sampling is disabled in Staging to avoid dropping security/audit logs.

### appsettings.Production.json

Production overrides:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Chat.Web": "Warning"
    }
  },
  "ApplicationInsights": {
    "SamplingSettings": {
      "IsEnabled": false
    }
  }
}
```

Notes:
- Production defaults to `Warning`+ to reduce noise and cost.
- Sampling is disabled in Production to avoid dropping security/audit logs.

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
