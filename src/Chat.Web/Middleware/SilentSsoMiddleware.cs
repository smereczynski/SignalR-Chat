using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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

        private bool ShouldAttemptSilentSso(HttpContext context)
        {
            // Skip if Entra ID not enabled
            if (!_entraOptions.IsEnabled || _entraOptions.AutomaticSso?.Enable != true)
                return false;

            // Already authenticated -> skip
            if (context.User?.Identity?.IsAuthenticated == true)
                return false;

            // Check if authentication cookie exists (prevents re-challenging after OIDC callback)
            if (context.Request.Cookies.ContainsKey(".AspNetCore.Cookies"))
                return false;

            // Only GET requests eligible
            if (!HttpMethods.IsGet(context.Request.Method))
                return false;

            // Check path eligibility
            if (!IsEligiblePath(context.Request.Path.Value))
                return false;

            // Prevent loops if attempt recorded
            var cookieName = _entraOptions.AutomaticSso.AttemptCookieName ?? "sso_attempted";
            if (_entraOptions.AutomaticSso.AttemptOncePerSession && context.Request.Cookies.ContainsKey(cookieName))
                return false;

            return true;
        }

        private static bool IsEligiblePath(string path)
        {
            var normalizedPath = path?.ToLowerInvariant() ?? string.Empty;
            
            // Never trigger on auth pages to avoid loops
            if (normalizedPath.StartsWith("/login") || 
                normalizedPath.StartsWith("/signin") || 
                normalizedPath.StartsWith("/signout"))
                return false;
            
            // Eligible entry points: root or chat pages
            return normalizedPath == "/" || 
                   normalizedPath.StartsWith("/chat") || 
                   normalizedPath.StartsWith("/chat/");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!ShouldAttemptSilentSso(context))
            {
                await _next(context);
                return;
            }

            var cookieName = _entraOptions.AutomaticSso.AttemptCookieName ?? "sso_attempted";
            var path = context.Request.Path.Value ?? "/";

            try
            {
                // Mark attempt (short lifetime)
                context.Response.Cookies.Append(cookieName, "1", new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(10)
                });

                _logger.LogDebug("Silent SSO attempt initiated for path {Path}", path);

                var props = new AuthenticationProperties
                {
                    RedirectUri = context.Request.Path + context.Request.QueryString
                };
                props.Parameters["silent"] = true; // consumed in OIDC OnRedirectToIdentityProvider
                props.Items["silent"] = "true"; // persists through OAuth callback for OnRemoteFailure

                // Resolve the default challenge scheme to avoid hardcoding scheme names
                var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
                var defaultChallenge = await schemeProvider.GetDefaultChallengeSchemeAsync();
                var challengeScheme = defaultChallenge?.Name ?? "EntraId"; // fallback for safety if not configured
                await context.ChallengeAsync(challengeScheme, props);
                return; // short-circuit pipeline until OIDC completes
            }
            catch (Microsoft.AspNetCore.Authentication.AuthenticationFailureException ex)
            {
                _logger.LogInformation(ex, "Silent SSO failed (expected in private/incognito mode), redirecting to login");
                // Redirect to login page with indication that silent SSO was attempted
                if (!context.Response.HasStarted)
                {
                    context.Response.Redirect("/login?reason=sso_failed");
                }
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Silent SSO attempt failed with unexpected error, continuing to normal pipeline");
                // Continue to login page or next middleware without failing request
                if (!context.Response.HasStarted)
                {
                    await _next(context);
                }
            }
        }
    }
}
