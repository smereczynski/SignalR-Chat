using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Chat.Web.Options;

namespace Chat.Web.Middleware
{
    /// <summary>
    /// Attempts a one-time silent Single Sign-On (SSO) OpenID Connect challenge (prompt=none)
    /// for users already authenticated with Microsoft Entra ID in the browser.
    /// Falls back immediately to the normal /login page if interaction is required or any error occurs.
    /// Guarded by a short-lived cookie to avoid loops.
    /// </summary>
    public class SilentSsoMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SilentSsoMiddleware> _logger;
        private readonly EntraIdOptions _entraOptions;

        public SilentSsoMiddleware(RequestDelegate next, ILogger<SilentSsoMiddleware> logger, IOptions<EntraIdOptions> entraOpts)
        {
            _next = next;
            _logger = logger;
            _entraOptions = entraOpts.Value;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip if Entra ID not enabled
            if (!_entraOptions.IsEnabled || _entraOptions.AutomaticSso?.Enable != true)
            {
                await _next(context);
                return;
            }

            // Already authenticated -> continue
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // Only GET requests eligible
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            // Eligible entry points: root or chat pages (adjust if needed)
            var eligible = path == "/" || path.StartsWith("/chat") || path.StartsWith("/chat/");
            if (!eligible)
            {
                await _next(context);
                return;
            }

            // Prevent loops if attempt recorded
            var cookieName = _entraOptions.AutomaticSso.AttemptCookieName ?? "sso_attempted";
            if (_entraOptions.AutomaticSso.AttemptOncePerSession && context.Request.Cookies.ContainsKey(cookieName))
            {
                await _next(context);
                return;
            }

            try
            {
                // Mark attempt (short lifetime)
                context.Response.Cookies.Append(cookieName, "1", new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(10)
                });

                _logger.LogDebug("Silent SSO attempt initiated for path {Path}", path);

                var props = new AuthenticationProperties
                {
                    RedirectUri = context.Request.Path + context.Request.QueryString
                };
                props.Parameters["silent"] = true; // consumed in OIDC OnRedirectToIdentityProvider

                await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, props);
                return; // short-circuit pipeline until OIDC completes
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Silent SSO attempt failed early, continuing to normal pipeline");
                // Continue to login page or next middleware without failing request
                await _next(context);
            }
        }
    }
}
