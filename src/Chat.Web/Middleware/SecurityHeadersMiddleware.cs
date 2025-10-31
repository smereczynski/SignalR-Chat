using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Chat.Web.Middleware
{
    /// <summary>
    /// Middleware that adds security headers to all responses, including Content Security Policy (CSP),
    /// X-Content-Type-Options, X-Frame-Options, and Referrer-Policy.
    /// 
    /// For HTML responses, generates a cryptographically secure nonce per request that can be used
    /// in inline scripts via @Context.Items["csp-nonce"].
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Generate nonce for this request (used in inline scripts)
            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToBase64String(nonceBytes);
            context.Items["csp-nonce"] = nonce;

            // Set security headers before processing the request
            var headers = context.Response.Headers;

            // Content Security Policy (CSP)
            // - default-src 'self': Only allow resources from same origin by default
            // - script-src 'self' 'nonce-{nonce}': Allow scripts from same origin + inline scripts with nonce
            // - style-src 'self' 'unsafe-inline': Allow styles from same origin + inline styles (Bootstrap modals need this)
            // - connect-src 'self' wss: https:: Allow same origin + WebSocket (for SignalR) + HTTPS connections
            // - img-src 'self' data: https:: Allow same origin + data URIs + HTTPS images
            // - font-src 'self' data:: Allow same origin + data URIs (for custom fonts)
            // - frame-ancestors 'none': Prevent clickjacking (don't allow embedding in frames)
            // - base-uri 'self': Restrict <base> tag to same origin
            // - form-action 'self': Only allow form submissions to same origin
            headers["Content-Security-Policy"] = 
                $"default-src 'self'; " +
                $"script-src 'self' 'nonce-{nonce}'; " +
                $"style-src 'self' 'unsafe-inline'; " +
                $"connect-src 'self' wss: https:; " +
                $"img-src 'self' data: https:; " +
                $"font-src 'self' data:; " +
                $"frame-ancestors 'none'; " +
                $"base-uri 'self'; " +
                $"form-action 'self'";

            // Additional security headers
            headers["X-Content-Type-Options"] = "nosniff"; // Prevent MIME type sniffing
            headers["X-Frame-Options"] = "DENY"; // Defense in depth (also covered by CSP frame-ancestors)
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin"; // Control referrer information

            await _next(context);
        }
    }
}
