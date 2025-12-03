#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chat.Web.Services;

/// <summary>
/// Azure AI Translator service implementation using REST API (2025-10-01-preview).
/// Supports Redis caching for translations with configurable TTL.
/// Always translates to EN + other language(s), with EN translation always stored.
/// Never logs user input text for security and privacy.
/// </summary>
public class AzureTranslatorService : ITranslationService
{
    private readonly HttpClient _httpClient;
    private readonly TranslationOptions _options;
    private readonly ILogger<AzureTranslatorService> _logger;
    private readonly IDatabase? _redis;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string CachePrefix = "translation:";

    public AzureTranslatorService(
        HttpClient httpClient,
        IOptions<TranslationOptions> options,
        ILogger<AzureTranslatorService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _redis = redis?.GetDatabase();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Translation service is disabled");
            throw new InvalidOperationException("Translation service is disabled");
        }

        if (string.IsNullOrEmpty(_options.Endpoint))
        {
            _logger.LogError("Translation endpoint is not configured");
            throw new InvalidOperationException("Translation endpoint is not configured");
        }

        if (string.IsNullOrEmpty(_options.SubscriptionKey))
        {
            _logger.LogError("Translation subscription key is not configured");
            throw new InvalidOperationException("Translation subscription key is not configured");
        }

        if (request.Targets.Count == 0)
        {
            throw new ArgumentException("At least one target language is required", nameof(request));
        }

        // Validate that EN is always included in targets
        var hasEnglish = request.Targets.Any(t => t.Language.Equals("en", StringComparison.OrdinalIgnoreCase));
        if (!hasEnglish)
        {
            throw new ArgumentException("English (en) must always be included in translation targets", nameof(request));
        }

        try
        {
            // Try cache first (unless ForceRefresh is true)
            if (!request.ForceRefresh && _redis != null && _options.CacheTtlSeconds > 0)
            {
                var cacheResult = await TryGetFromCacheAsync(request).ConfigureAwait(false);
                if (cacheResult != null)
                {
                    _logger.LogDebug(
                        "Translation cache hit for {TargetCount} languages (source: {SourceLanguage})",
                        request.Targets.Count,
                        request.SourceLanguage ?? "auto-detect");
                    return cacheResult;
                }
            }

            // Cache miss or ForceRefresh - call API
            _logger.LogDebug(
                "Translating to {TargetCount} languages (source: {SourceLanguage}, ForceRefresh: {ForceRefresh})",
                request.Targets.Count,
                request.SourceLanguage ?? "auto-detect",
                request.ForceRefresh);

            var response = await CallTranslationApiAsync(request, cancellationToken).ConfigureAwait(false);

            // Store in cache (best effort - don't fail if caching fails)
            if (_redis != null && _options.CacheTtlSeconds > 0)
            {
                await TrySetCacheAsync(request, response).ConfigureAwait(false);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Translation API HTTP request failed: StatusCode={StatusCode}, Message={Message}",
                ex.StatusCode,
                ex.Message);
            throw new InvalidOperationException("Translation API request failed", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Translation API request timeout");
            throw new InvalidOperationException("Translation API request timeout", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
        {
            _logger.LogError(ex, "Translation request failed with unexpected error: {ErrorType}", ex.GetType().Name);
            throw new InvalidOperationException("Translation request failed", ex);
        }
    }

    private async Task<TranslateResponse> CallTranslationApiAsync(
        TranslateRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = _options.Endpoint.TrimEnd('/');
        var url = $"{endpoint}/translator/text/translate?api-version={_options.ApiVersion}";

        // Build API request matching nested targets structure
        var apiRequest = new TranslateApiRequest
        {
            Inputs = new[]
            {
                new TranslateInput
                {
                    Text = request.Text,
                    Language = request.SourceLanguage,
                    Targets = request.Targets.Select(t => new TranslateTarget
                    {
                        Language = t.Language,
                        DeploymentName = t.DeploymentName ?? _options.DeploymentName,
                        Tone = request.Tone
                    }).ToArray()
                }
            }
        };

        var requestJson = JsonSerializer.Serialize(apiRequest, _jsonOptions);
        
        _logger.LogDebug("Translation API request: {Url}", url);
        _logger.LogDebug("Translation API request body: {RequestJson}", requestJson);
        
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        // Build HTTP request
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };

        httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);

        if (!string.IsNullOrEmpty(_options.Region))
        {
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Region", _options.Region);
        }

        // Execute request with retry logic
        using var httpResponse = await RetryHelper.ExecuteAsync(
            async _ => await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false),
            Transient.IsHttpTransient,
            _logger,
            "translation.api.call",
            maxAttempts: 3,
            baseDelayMs: 500,
            perAttemptTimeoutMs: 10000).ConfigureAwait(false);

        // Read response
        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Translation API returned error status {StatusCode}: {ResponseBody}",
                httpResponse.StatusCode,
                responseJson);

            httpResponse.EnsureSuccessStatusCode();
        }

        // Parse response
        var apiResponse = JsonSerializer.Deserialize<TranslateApiResponse>(responseJson, _jsonOptions);

        if (apiResponse?.Value == null || apiResponse.Value.Length == 0)
        {
            _logger.LogError("Translation API returned empty response");
            throw new InvalidOperationException("Translation API returned empty response");
        }

        var result = apiResponse.Value[0];

        _logger.LogInformation(
            "Translation completed: {TranslationCount} translations, detected language: {DetectedLanguage} (score: {Score:F2})",
            result.Translations?.Length ?? 0,
            result.DetectedLanguage?.Language ?? "none",
            result.DetectedLanguage?.Score ?? 0.0);

        return new TranslateResponse
        {
            DetectedLanguage = result.DetectedLanguage?.Language,
            DetectedLanguageScore = result.DetectedLanguage?.Score ?? 0.0,
            Translations = result.Translations?.Select(t => new Translation
            {
                Text = t.Text,
                Language = t.Language,
                SourceCharacters = t.SourceCharacters,
                InstructionTokens = t.InstructionTokens,
                SourceTokens = t.SourceTokens,
                TargetTokens = t.TargetTokens
            }).ToArray() ?? Array.Empty<Translation>(),
            FromCache = false
        };
    }

    private async Task<TranslateResponse?> TryGetFromCacheAsync(
        TranslateRequest request)
    {
        if (_redis == null)
        {
            return null;
        }

        try
        {
            var cacheKey = GenerateCacheKey(request);

            var cachedJson = await RetryHelper.ExecuteAsync(
                _ => _redis.StringGetAsync(cacheKey),
                Transient.IsRedisTransient,
                _logger,
                "translation.cache.get",
                maxAttempts: 2,
                baseDelayMs: 100,
                perAttemptTimeoutMs: 1000).ConfigureAwait(false);

            if (cachedJson.IsNullOrEmpty)
            {
                return null;
            }

            var cachedResponse = JsonSerializer.Deserialize<TranslateResponse>((string)cachedJson!, _jsonOptions);
            if (cachedResponse != null)
            {
                return new TranslateResponse
                {
                    DetectedLanguage = cachedResponse.DetectedLanguage,
                    DetectedLanguageScore = cachedResponse.DetectedLanguageScore,
                    Translations = cachedResponse.Translations,
                    FromCache = true
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve translation from cache (non-fatal): {ErrorType}", ex.GetType().Name);
            return null; // Cache failure should not block translation
        }
    }

    private async Task TrySetCacheAsync(
        TranslateRequest request,
        TranslateResponse response)
    {
        if (_redis == null)
        {
            return;
        }

        try
        {
            var cacheKey = GenerateCacheKey(request);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var ttl = TimeSpan.FromSeconds(_options.CacheTtlSeconds);

            await RetryHelper.ExecuteAsync(
                _ => _redis.StringSetAsync(cacheKey, json, ttl),
                Transient.IsRedisTransient,
                _logger,
                "translation.cache.set",
                maxAttempts: 2,
                baseDelayMs: 100,
                perAttemptTimeoutMs: 1000).ConfigureAwait(false);

            _logger.LogDebug("Cached translation result for {TtlSeconds}s", _options.CacheTtlSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache translation result (non-fatal): {ErrorType}", ex.GetType().Name);
            // Cache failure should not fail the translation request
        }
    }

    /// <summary>
    /// Generate deterministic cache key from request.
    /// Uses SHA256 hash of: text + sourceLanguage + sorted(targets) + tone
    /// NEVER logs the actual text content for security.
    /// </summary>
    private string GenerateCacheKey(TranslateRequest request)
    {
        var keyComponents = new StringBuilder();
        keyComponents.Append(request.Text);
        keyComponents.Append('|');
        keyComponents.Append(request.SourceLanguage ?? "auto");
        keyComponents.Append('|');

        // Sort targets for deterministic key (order shouldn't matter)
        var sortedTargets = request.Targets
            .OrderBy(t => t.Language)
            .ThenBy(t => t.DeploymentName ?? "");

        foreach (var target in sortedTargets)
        {
            keyComponents.Append(target.Language);
            keyComponents.Append(':');
            keyComponents.Append(target.DeploymentName ?? "default");
            keyComponents.Append(';');
        }

        if (!string.IsNullOrEmpty(request.Tone))
        {
            keyComponents.Append('|');
            keyComponents.Append(request.Tone);
        }

        // Hash to fixed-length key (SHA256 = 64 hex chars)
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyComponents.ToString()));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return $"{CachePrefix}{hashHex}";
    }

    #region API DTOs (Internal - matches Azure API 2025-10-01-preview)

    private class TranslateApiRequest
    {
        [JsonPropertyName("inputs")]
        public TranslateInput[] Inputs { get; set; } = Array.Empty<TranslateInput>();
    }

    private class TranslateInput
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("targets")]
        public TranslateTarget[] Targets { get; set; } = Array.Empty<TranslateTarget>();
    }

    private class TranslateTarget
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("deploymentName")]
        public string? DeploymentName { get; set; }

        [JsonPropertyName("tone")]
        public string? Tone { get; set; }
    }

    private class TranslateApiResponse
    {
        [JsonPropertyName("value")]
        public TranslateApiValue[] Value { get; set; } = Array.Empty<TranslateApiValue>();
    }

    private class TranslateApiValue
    {
        [JsonPropertyName("detectedLanguage")]
        public DetectedLanguage? DetectedLanguage { get; set; }

        [JsonPropertyName("translations")]
        public TranslateApiTranslation[] Translations { get; set; } = Array.Empty<TranslateApiTranslation>();
    }

    private class DetectedLanguage
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }
    }

    private class TranslateApiTranslation
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("sourceCharacters")]
        public int? SourceCharacters { get; set; }

        [JsonPropertyName("instructionTokens")]
        public int? InstructionTokens { get; set; }

        [JsonPropertyName("sourceTokens")]
        public int? SourceTokens { get; set; }

        [JsonPropertyName("targetTokens")]
        public int? TargetTokens { get; set; }
    }

    #endregion
}
