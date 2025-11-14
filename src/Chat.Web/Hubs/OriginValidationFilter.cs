using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Chat.Web.Hubs
{
    /// <summary>
    /// Hub filter that validates Origin header on SignalR hub connections.
    /// Provides defense-in-depth validation beyond CORS policy.
    /// Logs and blocks connections from invalid origins.
    /// </summary>
    public class OriginValidationFilter : IHubFilter
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OriginValidationFilter> _logger;

        public OriginValidationFilter(
            IConfiguration configuration,
            ILogger<OriginValidationFilter> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext invocationContext,
            Func<HubInvocationContext, ValueTask<object?>> next)
        {
            var context = invocationContext.Context.GetHttpContext();
            if (context != null)
            {
                var origin = context.Request.Headers["Origin"].ToString();
                var referer = context.Request.Headers["Referer"].ToString();

                // Only validate if Origin or Referer is present
                // (Missing headers = same-origin request, which is allowed)
                if (!string.IsNullOrEmpty(origin) || !string.IsNullOrEmpty(referer))
                {
                    if (!IsValidOrigin(origin ?? referer))
                    {
                        var userId = context.User?.Identity?.Name ?? "anonymous";
                        var connectionId = invocationContext.Context.ConnectionId;

                        _logger.LogWarning(
                            "SECURITY: Blocked SignalR hub method '{Method}' from invalid origin. " +
                            "Origin: {Origin}, Referer: {Referer}, User: {User}, ConnectionId: {ConnectionId}",
                            invocationContext.HubMethodName,
                            origin,
                            referer,
                            userId,
                            connectionId);

                        throw new HubException("Origin not allowed.");
                    }
                }
            }

            return await next(invocationContext);
        }

        public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            var httpContext = context.Context.GetHttpContext();
            if (httpContext != null)
            {
                var origin = httpContext.Request.Headers["Origin"].ToString();
                var referer = httpContext.Request.Headers["Referer"].ToString();

                // Validate origin on connection
                if (!string.IsNullOrEmpty(origin) || !string.IsNullOrEmpty(referer))
                {
                    if (!IsValidOrigin(origin ?? referer))
                    {
                        var userId = httpContext.User?.Identity?.Name ?? "anonymous";
                        var connectionId = context.Context.ConnectionId;

                        _logger.LogWarning(
                            "SECURITY: Blocked SignalR hub connection from invalid origin. " +
                            "Origin: {Origin}, Referer: {Referer}, User: {User}, ConnectionId: {ConnectionId}",
                            origin,
                            referer,
                            userId,
                            connectionId);

                        throw new HubException("Origin not allowed.");
                    }
                }

                _logger.LogDebug(
                    "SignalR connection established. Origin: {Origin}, User: {User}, ConnectionId: {ConnectionId}",
                    origin,
                    httpContext.User?.Identity?.Name ?? "anonymous",
                    context.Context.ConnectionId);
            }

            return next(context);
        }

        private bool IsValidOrigin(string originOrReferer)
        {
            // Development mode - allow all origins
            var allowAllOrigins = _configuration.GetValue<bool>("Cors:AllowAllOrigins");
            if (allowAllOrigins)
            {
                return true;
            }

            // Production/Staging - check whitelist
            var allowedOrigins = _configuration.GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            // Extract origin from referer if needed (referer includes path, origin is just scheme://host:port)
            var originToCheck = originOrReferer;
            if (Uri.TryCreate(originOrReferer, UriKind.Absolute, out var uri))
            {
                originToCheck = $"{uri.Scheme}://{uri.Authority}";
            }

            return allowedOrigins.Any(allowed =>
                string.Equals(allowed, originToCheck, StringComparison.OrdinalIgnoreCase));
        }
    }
}
