using System;

namespace Chat.Web.Options
{
    /// <summary>
    /// Configuration options for CORS (Cross-Origin Resource Sharing) policy.
    /// Used to restrict which origins can call SignalR hubs and API endpoints.
    /// </summary>
    public class CorsOptions
    {
        /// <summary>
        /// List of allowed origins (e.g., https://yourdomain.com).
        /// Used in Production/Staging for strict origin validation.
        /// </summary>
        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

        /// <summary>
        /// If true, allows all origins (*.SetIsOriginAllowed(_ => true)).
        /// ONLY use in Development environment for easier testing.
        /// MUST be false in Production.
        /// </summary>
        public bool AllowAllOrigins { get; set; } = false;
    }
}
