namespace Chat.Web.Models;

/// <summary>
/// Specific translation failure codes.
/// </summary>
public enum TranslationFailureCode
{
    Unknown = 0,

    // Configuration / validation
    Disabled = 10,
    MissingEndpoint = 11,
    MissingSubscriptionKey = 12,
    InvalidTargetLanguage = 13,
    InvalidTargets = 14,

    // API / network
    Unauthorized = 100,
    Forbidden = 101,
    NotFound = 102,
    RateLimited = 103,
    Timeout = 104,
    ServiceUnavailable = 105,
    NetworkError = 106,
    HttpError = 107,

    // Content
    BadRequest = 200,
    EmptyResponse = 201
}
