namespace Chat.Web.Models;

/// <summary>
/// Coarse classification of translation failures.
/// </summary>
public enum TranslationFailureCategory
{
    /// <summary>
    /// Unknown or not classified.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Configuration or request validation problems (non-retryable).
    /// </summary>
    Configuration = 1,

    /// <summary>
    /// Translator API/service or network problems.
    /// </summary>
    Api = 2,

    /// <summary>
    /// Content/request-related issues that are unlikely to succeed on retry.
    /// </summary>
    Content = 3
}
