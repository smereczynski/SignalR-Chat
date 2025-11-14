# ADR 0001: SignalR CORS and Origin Validation

**Date**: 2025-11-14  
**Status**: Accepted  
**Issue**: [#63 - Add Origin Validation for SignalR Hub Negotiate Endpoint](https://github.com/smereczynski/SignalR-Chat/issues/63)

## Context

SignalR Chat uses cookie-based authentication with a public SignalR hub endpoint (`/chatHub`). Without origin validation, malicious websites can establish authenticated hub connections using victims' cookies, enabling Cross-Site Request Forgery (CSRF) attacks.

### The Problem

Before this decision:
- **No CORS policy** configured in ASP.NET Core application
- **No origin validation** on SignalR hub endpoint
- Cookie-based authentication is vulnerable to CSRF
- Malicious websites could:
  - Establish SignalR connections as victim
  - Send messages on behalf of victim
  - Read real-time data from chat rooms
  - Manipulate presence and typing indicators

### Attack Scenario

```javascript
// Malicious website (https://evil.com)
const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://app-signalrchat-prod.azurewebsites.net/chatHub")
  .build();

await connection.start(); // Uses victim's cookies automatically
await connection.invoke("SendMessage", "general", "I love evil.com!"); // Sent as victim
```

### Requirements

1. **Browser-enforced protection**: Use standard CORS policy for preflight checks
2. **Server-side defense in depth**: Validate origins even if CORS bypassed
3. **Environment-specific**: Permissive in development, strict in production
4. **Logging**: Track and alert on invalid origin attempts
5. **Minimal overhead**: No performance impact on legitimate requests

## Decision

We will implement a **hybrid approach** combining CORS policy with hub-level validation:

### 1. CORS Policy (Primary Defense)

**Implementation**: ASP.NET Core CORS middleware with configuration-driven origins

```csharp
services.AddCors(options =>
{
    options.AddPolicy("SignalRPolicy", builder =>
    {
        if (corsOptions.AllowAllOrigins) // Development only
        {
            builder.SetIsOriginAllowed(_ => true)
                   .AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials();
        }
        else // Production/Staging
        {
            builder.WithOrigins(corsOptions.AllowedOrigins)
                   .AllowAnyHeader()
                   .AllowAnyMethod()
                   .AllowCredentials();
        }
    });
});

// Apply to SignalR hub
app.UseCors("SignalRPolicy");
endpoints.MapHub<ChatHub>("/chatHub").RequireCors("SignalRPolicy");
```

**Configuration** (`appsettings.json`):

```json
{
  "Cors": {
    "AllowedOrigins": ["https://app-signalrchat-prod-weu.azurewebsites.net"],
    "AllowAllOrigins": false
  }
}
```

### 2. Origin Validation Filter (Defense in Depth)

**Implementation**: Custom `IHubFilter` for server-side validation

```csharp
public class OriginValidationFilter : IHubFilter
{
    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var origin = httpContext.Request.Headers["Origin"].ToString();
        var referer = httpContext.Request.Headers["Referer"].ToString();
        
        if (!string.IsNullOrEmpty(origin) && !IsValidOrigin(origin))
        {
            _logger.LogWarning(
                "SECURITY: Blocked SignalR hub connection from invalid origin: {Origin}",
                origin);
            throw new HubException("Origin not allowed.");
        }
        
        return next(context);
    }
}
```

### 3. Configuration Validation

**Startup guard**: Prevents accidental production misconfig uration

```csharp
if (!HostEnvironment.IsDevelopment() && corsOptions.AllowAllOrigins)
{
    throw new InvalidOperationException(
        "Cors:AllowAllOrigins is set to true in non-Development environment. " +
        "This is a security risk. Set to false and configure Cors:AllowedOrigins instead.");
}
```

## Alternatives Considered

### Option 1: SignalR Hub Options Only

```csharp
services.AddSignalR().AddHubOptions<ChatHub>(options =>
{
    options.AllowedOrigins.Add("https://yourdomain.com");
});
```

**Pros**:
- Simple implementation
- SignalR-specific configuration

**Cons**:
- ❌ Not a standard CORS implementation
- ❌ Doesn't support browser preflight checks
- ❌ Limited to SignalR, doesn't protect other endpoints
- ❌ Less flexible configuration

### Option 2: Custom Middleware Only

```csharp
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/chatHub"))
    {
        var origin = context.Request.Headers["Origin"].ToString();
        if (!IsValidOrigin(origin))
        {
            context.Response.StatusCode = 403;
            return;
        }
    }
    await next();
});
```

**Pros**:
- Highly flexible
- Custom logging and error handling

**Cons**:
- ❌ Bypasses standard CORS flow
- ❌ No browser preflight support
- ❌ Manual header management
- ❌ Reinventing the wheel

### Option 3: CORS Policy Only (Considered but Insufficient)

**Pros**:
- Industry standard
- Browser-enforced
- Preflight support

**Cons**:
- ❌ No defense in depth (can be bypassed with browser extensions, proxies)
- ❌ No server-side logging of blocked attempts
- ❌ Relies entirely on browser behavior

## Consequences

### Positive

✅ **Security**:
- Prevents CSRF attacks on SignalR hub
- Defense in depth with dual validation layers
- Logging enables security monitoring and alerting

✅ **Standards Compliance**:
- Uses industry-standard CORS mechanism
- Browser preflight checks work correctly
- Compatible with CDNs and reverse proxies

✅ **Developer Experience**:
- Development mode allows all origins (easy local testing)
- Configuration-driven (no code changes for new domains)
- Clear error messages when misconfigured

✅ **Operations**:
- Security warnings logged to Application Insights
- Failed origin attempts tracked as metrics
- Startup validation prevents deployment errors

### Negative

⚠️ **Configuration Complexity**:
- Requires accurate CORS configuration in appsettings.json
- Custom domains must be manually added to allowed origins
- Risk of misconfiguration if not validated

⚠️ **Maintenance**:
- Need to update allowed origins when adding new domains
- Configuration differs per environment (dev/staging/prod)

### Mitigation

- **Startup validation**: Throws exception if `AllowAllOrigins=true` in production
- **Documentation**: Clear examples in README.md and configuration guide
- **Environment-specific configs**: Separate appsettings files prevent confusion
- **Logging**: Console warnings in development mode about permissive CORS

## Implementation

**Files Created**:
- `src/Chat.Web/Options/CorsOptions.cs` - Configuration class
- `src/Chat.Web/Hubs/OriginValidationFilter.cs` - Hub filter
- `src/Chat.Web/appsettings.Staging.json` - Staging configuration
- `tests/Chat.IntegrationTests/CorsValidationTests.cs` - Integration tests

**Files Modified**:
- `src/Chat.Web/Startup.cs` - CORS policy and middleware
- `src/Chat.Web/appsettings.Development.json` - Development configuration
- `src/Chat.Web/appsettings.Production.json` - Production configuration
- `README.md` - CORS configuration documentation

**Testing**:
- 6 integration tests cover CORS validation scenarios
- All 124 existing tests pass (no breaking changes)

## References

- **Issue**: [#63 - Add Origin Validation for SignalR Hub Negotiate Endpoint](https://github.com/smereczynski/SignalR-Chat/issues/63)
- **Microsoft Docs**: [Enable CORS in ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/cors)
- **SignalR Docs**: [Security considerations in ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/security)
- **OWASP**: [Cross-Site Request Forgery (CSRF)](https://owasp.org/www-community/attacks/csrf)
- **Project Guidance**: `.github/copilot-instructions.md` - Security best practices

## Notes

- Azure SignalR Service also has CORS configuration at infrastructure level (configured in Bicep)
- Redis and Cosmos DB are backend services (not exposed to browsers) - no CORS needed
- This decision addresses immediate CSRF risk; future Entra ID authentication (issue #101) will add additional security layers

---

**Approved By**: @smereczynski  
**Implementation Date**: 2025-11-14  
**Review Date**: 2026-01-14 (2 months)
