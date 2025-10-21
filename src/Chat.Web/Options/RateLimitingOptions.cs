namespace Chat.Web.Options
{
    /// <summary>
    /// Configuration for rate limiting on MarkRead hub operations.
    /// Protects against abuse/DOS attacks that could saturate the database.
    /// </summary>
    public class RateLimitingOptions
    {
        /// <summary>
        /// Maximum number of MarkRead operations allowed per user within the time window.
        /// Default: 100 operations.
        /// </summary>
        public int MarkReadPermitLimit { get; set; } = 100;

        /// <summary>
        /// Time window in seconds for the rate limit.
        /// Default: 10 seconds (100 marks per 10 seconds).
        /// </summary>
        public int MarkReadWindowSeconds { get; set; } = 10;
    }
}
