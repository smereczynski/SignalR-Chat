using System.Collections.Generic;

namespace Chat.Web.Options
{
    /// <summary>
    /// Configuration for Microsoft Entra ID (Azure AD) multi-tenant authentication.
    /// </summary>
    public class EntraIdOptions
    {
        /// <summary>
        /// Microsoft identity platform instance URL. Default: https://login.microsoftonline.com/
        /// </summary>
        public string Instance { get; set; } = "https://login.microsoftonline.com/";

        /// <summary>
        /// Tenant ID for multi-tenant apps. Use "organizations" for any organizational account.
        /// </summary>
        public string TenantId { get; set; } = "organizations";

        /// <summary>
        /// Application (client) ID from Entra ID app registration.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Client secret from app registration. NOT stored in appsettings - use connection string or Key Vault.
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Callback path for OpenID Connect sign-in. Default: /signin-oidc
        /// </summary>
        public string CallbackPath { get; set; } = "/signin-oidc";

        /// <summary>
        /// Callback path after sign-out. Default: /signout-oidc
        /// </summary>
        public string SignedOutCallbackPath { get; set; } = "/signout-oidc";

        /// <summary>
        /// Authorization settings for tenant and user validation.
        /// </summary>
        public EntraIdAuthorizationOptions Authorization { get; set; } = new();

        /// <summary>
        /// Fallback options for OTP authentication.
        /// </summary>
        public EntraIdFallbackOptions Fallback { get; set; } = new();

        /// <summary>
        /// Automatic silent single sign-on (SSO) attempt options.
        /// When enabled, the app will try a one-time OpenID Connect challenge with prompt=none
        /// for already-signed-in Microsoft users hitting root/chat pages.
        /// </summary>
        public EntraIdAutomaticSsoOptions AutomaticSso { get; set; } = new();

        /// <summary>
        /// Gets whether Entra ID authentication is enabled (ClientId configured).
        /// </summary>
        public bool IsEnabled => !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>
    /// Authorization settings for Entra ID authentication.
    /// </summary>
    public class EntraIdAuthorizationOptions
    {
        /// <summary>
        /// List of allowed tenant domains or tenant IDs. Empty list = allow any tenant.
        /// Critical for security in multi-tenant scenarios.
        /// </summary>
        /// <remarks>
        /// Examples:
        /// - "contoso.onmicrosoft.com" (tenant domain)
        /// - "12345678-1234-1234-1234-123456789abc" (tenant GUID)
        /// </remarks>
        public List<string> AllowedTenants { get; set; } = new();

        /// <summary>
        /// Require user's tenant to be in AllowedTenants list. Default: true.
        /// If false, any tenant is allowed (authorization only via UPN in database).
        /// </summary>
        public bool RequireTenantValidation { get; set; } = true;
    }

    /// <summary>
    /// Fallback authentication options (OTP).
    /// </summary>
    public class EntraIdFallbackOptions
    {
        /// <summary>
        /// Enable OTP authentication as fallback/alternative to Entra ID. Default: true.
        /// </summary>
        public bool EnableOtp { get; set; } = true;

        /// <summary>
        /// Allow users unauthorized by Entra ID to fall back to OTP. Default: false.
        /// If true, users denied by tenant validation can still use OTP.
        /// If false, strict Entra ID enforcement (no fallback for unauthorized users).
        /// </summary>
        public bool OtpForUnauthorizedUsers { get; set; } = false;
    }

    /// <summary>
    /// Automatic silent SSO attempt configuration.
    /// </summary>
    public class EntraIdAutomaticSsoOptions
    {
        /// <summary>
        /// Enable automatic silent SSO attempt for first unauthenticated visit
        /// to root ("/") or /chat pages. Default: false (explicit login page).
        /// </summary>
        public bool Enable { get; set; } = false;

        /// <summary>
        /// If true, attempt silent SSO only once per browser session (tracked via short-lived cookie).
        /// Prevents loops or repeated OIDC round-trips. Default: true.
        /// </summary>
        public bool AttemptOncePerSession { get; set; } = true;

        /// <summary>
        /// Name of the guard cookie used to record an attempt. Default: sso_attempted.
        /// </summary>
        public string AttemptCookieName { get; set; } = "sso_attempted";
    }
}
