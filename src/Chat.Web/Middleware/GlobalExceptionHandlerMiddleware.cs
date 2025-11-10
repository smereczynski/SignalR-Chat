using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Middleware
{
    /// <summary>
    /// Global exception handler middleware that catches unhandled exceptions,
    /// logs them with full context, and returns a generic error response to the client.
    /// </summary>
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Log the exception with full context
                _logger.LogError(ex,
                    "Unhandled exception in {Method} {Path}. " +
                    "User: {User}, RemoteIP: {RemoteIP}, UserAgent: {UserAgent}, " +
                    "ExceptionType: {ExceptionType}, Message: {Message}",
                    context.Request.Method,
                    context.Request.Path,
                    context.User?.Identity?.Name ?? "Anonymous",
                    context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                    context.Request.Headers["User-Agent"].ToString(),
                    ex.GetType().FullName,
                    ex.Message);

                // Return a generic error response
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                error = "An unexpected error occurred. Please try again later.",
                requestId = context.TraceIdentifier
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
