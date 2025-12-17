using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Services;
using Xunit;

namespace Chat.Tests;

public class TranslationFailureClassifierTests
{
    [Fact]
    public void Classify_ArgumentException_IsConfiguration_InvalidTargets_NotRetryable()
    {
        var info = TranslationFailureClassifier.Classify(new ArgumentException("bad"));

        Assert.Equal(TranslationFailureCategory.Configuration, info.Category);
        Assert.Equal(TranslationFailureCode.InvalidTargets, info.Code);
        Assert.False(info.IsRetryable);
        Assert.False(string.IsNullOrWhiteSpace(info.SafeMessage));
    }

    [Fact]
    public void Classify_MissingEndpoint_IsConfiguration_MissingEndpoint_NotRetryable()
    {
        var info = TranslationFailureClassifier.Classify(new InvalidOperationException("Translation endpoint is not configured"));

        Assert.Equal(TranslationFailureCategory.Configuration, info.Category);
        Assert.Equal(TranslationFailureCode.MissingEndpoint, info.Code);
        Assert.False(info.IsRetryable);
    }

    [Fact]
    public void Classify_TaskCanceled_IsApi_Timeout_Retryable()
    {
        var info = TranslationFailureClassifier.Classify(new InvalidOperationException("Translation API request timeout", new TaskCanceledException()));

        Assert.Equal(TranslationFailureCategory.Api, info.Category);
        Assert.Equal(TranslationFailureCode.Timeout, info.Code);
        Assert.True(info.IsRetryable);
    }

    [Fact]
    public void Classify_Http429_IsApi_RateLimited_Retryable()
    {
        var ex = new HttpRequestException("rate limited", null, (HttpStatusCode)429);
        var info = TranslationFailureClassifier.Classify(new InvalidOperationException("Translation API request failed", ex));

        Assert.Equal(TranslationFailureCategory.Api, info.Category);
        Assert.Equal(TranslationFailureCode.RateLimited, info.Code);
        Assert.True(info.IsRetryable);
    }

    [Fact]
    public void Classify_Http400_IsContent_BadRequest_NotRetryable()
    {
        var ex = new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);
        var info = TranslationFailureClassifier.Classify(ex);

        Assert.Equal(TranslationFailureCategory.Content, info.Category);
        Assert.Equal(TranslationFailureCode.BadRequest, info.Code);
        Assert.False(info.IsRetryable);
    }

    [Fact]
    public void Classify_HttpNoStatus_IsApi_NetworkError_Retryable()
    {
        var ex = new HttpRequestException("network down");
        var info = TranslationFailureClassifier.Classify(ex);

        Assert.Equal(TranslationFailureCategory.Api, info.Category);
        Assert.Equal(TranslationFailureCode.NetworkError, info.Code);
        Assert.True(info.IsRetryable);
    }
}
