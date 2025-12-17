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
            return InvalidTargets();
        }

        if (exception is InvalidOperationException ioe && TryClassifyInvalidOperation(ioe, out var fromInvalidOp))
        {
            return fromInvalidOp;
        }

        // Walk inner exceptions to find HTTP/timeout root cause.
        var chain = Enumerate(exception).ToList();

        if (chain.Any(static ex => ex is TaskCanceledException))
        {
            return Timeout();
        }

        if (TryClassifyHttpRequest(chain, out var fromHttp))
        {
            return fromHttp;
        }

        return Unknown();
    }

    private static TranslationFailureInfo InvalidTargets() => new(
        TranslationFailureCategory.Configuration,
        TranslationFailureCode.InvalidTargets,
        "Translation failed due to invalid translation request.",
        IsRetryable: false);

    private static TranslationFailureInfo Timeout() => new(
        TranslationFailureCategory.Api,
        TranslationFailureCode.Timeout,
        "Translation failed (request timeout).",
        IsRetryable: true);

    private static TranslationFailureInfo Unknown() => new(
        TranslationFailureCategory.Unknown,
        TranslationFailureCode.Unknown,
        "Translation failed.",
        IsRetryable: true);

    private static bool TryClassifyInvalidOperation(InvalidOperationException exception, out TranslationFailureInfo info)
    {
        // AzureTranslatorService uses InvalidOperationException for configuration + API errors.
        var msg = exception.Message ?? string.Empty;

        if (msg.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            info = new TranslationFailureInfo(
                TranslationFailureCategory.Configuration,
                TranslationFailureCode.Disabled,
                "Translation is disabled.",
                IsRetryable: false);
            return true;
        }

        if (msg.Contains("endpoint is not configured", StringComparison.OrdinalIgnoreCase))
        {
            info = new TranslationFailureInfo(
                TranslationFailureCategory.Configuration,
                TranslationFailureCode.MissingEndpoint,
                "Translation is not configured (missing endpoint).",
                IsRetryable: false);
            return true;
        }

        if (msg.Contains("subscription key is not configured", StringComparison.OrdinalIgnoreCase))
        {
            info = new TranslationFailureInfo(
                TranslationFailureCategory.Configuration,
                TranslationFailureCode.MissingSubscriptionKey,
                "Translation is not configured (missing subscription key).",
                IsRetryable: false);
            return true;
        }

        if (msg.Contains("returned empty response", StringComparison.OrdinalIgnoreCase))
        {
            info = new TranslationFailureInfo(
                TranslationFailureCategory.Api,
                TranslationFailureCode.EmptyResponse,
                "Translation failed (empty response from service).",
                IsRetryable: true);
            return true;
        }

        if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            info = Timeout();
            return true;
        }

        info = default!;
        return false;
    }

    private static bool TryClassifyHttpRequest(
        System.Collections.Generic.IReadOnlyList<Exception> chain,
        out TranslationFailureInfo info)
    {
        var httpEx = chain.OfType<HttpRequestException>().FirstOrDefault();
        if (httpEx == null)
        {
            info = default!;
            return false;
        }

        if (httpEx.StatusCode is HttpStatusCode statusCode)
        {
            info = ClassifyHttpStatusCode(statusCode);
            return true;
        }

        info = new TranslationFailureInfo(
            TranslationFailureCategory.Api,
            TranslationFailureCode.NetworkError,
            "Translation failed (network error).",
            IsRetryable: true);
        return true;
    }

    private static TranslationFailureInfo ClassifyHttpStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Unauthorized, "Translation failed (unauthorized).", false),
        HttpStatusCode.Forbidden => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Forbidden, "Translation failed (forbidden).", false),
        HttpStatusCode.NotFound => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.NotFound, "Translation failed (service endpoint not found).", false),
        (HttpStatusCode)429 => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.RateLimited, "Translation failed (rate limited).", true),
        HttpStatusCode.RequestTimeout => Timeout(),
        HttpStatusCode.GatewayTimeout => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.Timeout, "Translation failed (gateway timeout).", true),
        HttpStatusCode.BadRequest => new TranslationFailureInfo(TranslationFailureCategory.Content, TranslationFailureCode.BadRequest, "Translation failed (invalid content/request).", false),
        HttpStatusCode.ServiceUnavailable => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.ServiceUnavailable, "Translation failed (service unavailable).", true),
        HttpStatusCode.BadGateway => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.ServiceUnavailable, "Translation failed (bad gateway).", true),
        HttpStatusCode.InternalServerError => new TranslationFailureInfo(TranslationFailureCategory.Api, TranslationFailureCode.ServiceUnavailable, "Translation failed (service error).", true),
        _ => new TranslationFailureInfo(
            TranslationFailureCategory.Api,
            TranslationFailureCode.HttpError,
            $"Translation failed (HTTP {(int)statusCode}).",
            IsRetryable: (int)statusCode >= 500)
    };

    private static System.Collections.Generic.IEnumerable<Exception> Enumerate(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
