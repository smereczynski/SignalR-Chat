using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Tests;

public class AzureTranslatorServiceValidationTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("HTTP should not be called for invalid requests");
        }
    }

    [Fact]
    public async Task TranslateAsync_TargetLanguageAuto_ThrowsBeforeHttpCall()
    {
        var handler = new CountingHandler();
        var httpClient = new HttpClient(handler);

        var options = Options.Create(new TranslationOptions
        {
            Enabled = true,
            Endpoint = "https://example.invalid",
            SubscriptionKey = "test-key",
            Region = "test-region",
            ApiVersion = "2025-10-01-preview",
            DeploymentName = "gpt-4o-mini"
        });

        var service = new AzureTranslatorService(httpClient, options, NullLogger<AzureTranslatorService>.Instance);

        var request = new TranslateRequest
        {
            Text = "hello",
            SourceLanguage = "en",
            Targets = new List<TranslationTarget>
            {
                new() { Language = "en" },
                new() { Language = "auto" }
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await service.TranslateAsync(request));
        Assert.Equal(0, handler.CallCount);
    }
}
