using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Chat.Web.Repositories; // for LogSanitizer

namespace Chat.Web.Observability
{
    public class RequestTracingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestTracingMiddleware> _logger;

        public RequestTracingMiddleware(RequestDelegate next, ILogger<RequestTracingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            // Avoid tracing static assets
            var path = context.Request.Path.HasValue ? context.Request.Path.Value : string.Empty;
            var isStatic = path.Contains('.') && !path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);
            Activity activity = null;
            if (!isStatic)
            {
                activity = Tracing.ActivitySource.StartActivity("http.request", ActivityKind.Server);
                if (activity != null)
                {
                    activity.SetTag("http.method", context.Request.Method);
                    activity.SetTag("http.path", path);
                }
            }

            try
            {
                await _next(context);
                if (activity != null)
                {
                    activity.SetTag("http.status_code", context.Response?.StatusCode);
                }
            }
            catch (Exception ex)
            {
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                }
                // Sanitize user-controlled values before logging to prevent log forging (CWE-117)
                var safeMethod = LogSanitizer.Sanitize(context.Request.Method, max: 16);
                var safePath = LogSanitizer.Sanitize(path);
                _logger.LogError(ex, "Unhandled exception processing \"{Method}\" \"{Path}\"", safeMethod, safePath);
                throw;
            }
            finally
            {
                if (activity != null)
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Headers["X-Trace-Id"] = activity.TraceId.ToString();
                        context.Response.Headers["X-Span-Id"] = activity.SpanId.ToString();
                    }
                    activity.Dispose();
                }
            }
        }
    }
}
