#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Services;

public sealed record TranslationFailureInfo(
    TranslationFailureCategory Category,
    TranslationFailureCode Code,
    string SafeMessage,
    bool IsRetryable);

/// <summary>
/// Classifies translation failures into stable categories and codes.
/// </summary>
public static class TranslationFailureClassifier
{
    public static TranslationFailureInfo Classify(Exception exception)
    {
        if (exception is ArgumentException)
        {
            return new TranslationFailureInfo(
                TranslationFailureCategory.Configuration,
                TranslationFailureCode.InvalidTargets,
                "Translation failed due to invalid translation request.",
                IsRetryable: false);
        }

        // AzureTranslatorService uses InvalidOperationException for configuration + API errors.
        if (exception is InvalidOperationException ioe)
        {
            var msg = ioe.Message ?? string.Empty;

            if (msg.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationFailureInfo(
                    TranslationFailureCategory.Configuration,
                    TranslationFailureCode.Disabled,
                    "Translation is disabled.",
                    IsRetryable: false);
            }

            if (msg.Contains("endpoint is not configured", StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationFailureInfo(
                    TranslationFailureCategory.Configuration,
                    TranslationFailureCode.MissingEndpoint,
                    "Translation is not configured (missing endpoint).",
                    IsRetryable: false);
            }

            if (msg.Contains("subscription key is not configured", StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationFailureInfo(
                    TranslationFailureCategory.Configuration,
                    TranslationFailureCode.MissingSubscriptionKey,
                    "Translation is not configured (missing subscription key).",
                    IsRetryable: false);
            }

            if (msg.Contains("returned empty response", StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationFailureInfo(
                    TranslationFailureCategory.Api,
                    TranslationFailureCode.EmptyResponse,
                    "Translation failed (empty response from service).",
                    IsRetryable: true);
            }

            if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationFailureInfo(
                    TranslationFailureCategory.Api,
                    TranslationFailureCode.Timeout,
                    "Translation failed (request timeout).",
                    IsRetryable: true);
            }
        }

        // Walk inner exceptions to find HTTP/timeout root cause.
        var all = Enumerate(exception).ToList();

        if (all.OfType<TaskCanceledException>().Any())
        {
            return new TranslationFailureInfo(
                TranslationFailureCategory.Api,
                TranslationFailureCode.Timeout,
                "Translation failed (request timeout).",
                IsRetryable: true);
        }

        var httpEx = all.OfType<HttpRequestException>().FirstOrDefault();
        if (httpEx != null)
        {
            if (httpEx.StatusCode is HttpStatusCode sc)
            {
                return sc switch
                {
                    HttpStatusCode.Unauthorized => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Unauthorized, "Translation failed (unauthorized).", false),
                    HttpStatusCode.Forbidden => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Forbidden, "Translation failed (forbidden).", false),
                    HttpStatusCode.NotFound => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.NotFound, "Translation failed (service endpoint not found).", false),
                    (HttpStatusCode)429 => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.RateLimited, "Translation failed (rate limited).", true),
                    HttpStatusCode.RequestTimeout => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Timeout, "Translation failed (request timeout).", true),
                    HttpStatusCode.GatewayTimeout => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Timeout, "Translation failed (gateway timeout).", true),
                    HttpStatusCode.BadRequest => new TranslationFailureInfo(TranslationFailureCategory.Content, TranslationFailureCode.BadRequest, "Translation failed (invalid content/request).", false),
                    HttpStatusCode.ServiceUnavailable => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.ServiceUnavailable, "Translation failed (service unavailable).", true),
                    HttpStatusCode.BadGateway => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.ServiceUnavailable, "Translation failed (bad gateway).", true),
                    HttpStatusCode.InternalServerError => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.ServiceUnavailable, "Translation failed (service error).", true),
                    _ => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.HttpError, $"Translation failed (HTTP {(int)sc}).", IsRetryable: (int)sc >= 500)
                };
            }

            return new TranslationFailureInfo(
                TranslationFailureCategory.Api,
                TranslationFailureCode.NetworkError,
                "Translation failed (network error).",
                IsRetryable: true);
        }

        return new TranslationFailureInfo(
            TranslationFailureCategory.Unknown,
            TranslationFailureCode.Unknown,
            "Translation failed.",
            IsRetryable: true);
    }

    private static System.Collections.Generic.IEnumerable<Exception> Enumerate(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
