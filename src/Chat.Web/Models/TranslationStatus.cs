namespace Chat.Web.Models
{
    /// <summary>
    /// Represents the translation status of a message.
    /// </summary>
    public enum TranslationStatus
    {
        /// <summary>
        /// No translation requested or translation service disabled.
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Translation job enqueued, waiting to be processed.
        /// </summary>
        Pending = 1,
        
        /// <summary>
        /// Translation is currently being processed by background service.
        /// </summary>
        InProgress = 2,
        
        /// <summary>
        /// Translation completed successfully.
        /// </summary>
        Completed = 3,
        
        /// <summary>
        /// Translation failed (API error, timeout, circuit breaker open).
        /// Manual retry available.
        /// </summary>
        Failed = 4
    }
}
