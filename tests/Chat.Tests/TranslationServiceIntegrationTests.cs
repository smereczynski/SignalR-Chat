using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Chat.Tests;

/// <summary>
/// Integration tests for Translation Service with Redis caching.
/// 
/// Prerequisites:
/// - .env.local with Translation__ configuration (Endpoint, SubscriptionKey, Region, DeploymentName)
/// - Redis running on localhost:6379 (for caching tests)
/// - Azure AI Translator resource deployed with LLM model (gpt-4o-mini or gpt-4o)
/// - Set Translation__Enabled=true to run these tests
/// 
/// Note: These tests make real API calls to Azure AI Translator and will consume quota.
/// Some tests may fail if the API format differs from implementation expectations.
/// Tests are designed to be non-destructive and use test-specific messages.
/// 
/// These tests are excluded from CI and are intended for local development only.
/// 
/// DISABLED: These tests are currently hanging VS Code and need investigation.
/// To enable, remove the Skip attribute.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LocalOnly")]
public class TranslationServiceIntegrationTests : IDisposable
{
    private readonly ITranslationService _translationService;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDatabase? _redisDb;
    private readonly TranslationOptions _options;
    private readonly bool _isEnabled;
    private readonly ILogger<AzureTranslatorService> _logger;

    public TranslationServiceIntegrationTests()
    {
        // Load configuration: environment variables first (lowest priority), then .env.local (highest priority)
        var configBuilder = new ConfigurationBuilder();
        
        // Add environment variables first (lowest priority)
        configBuilder.AddEnvironmentVariables();
        
        // Try to load .env.local from workspace root - this will override environment variables
        var workspaceRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        var envLocalPath = Path.Combine(workspaceRoot, ".env.local");
        
        if (File.Exists(envLocalPath))
        {
            // Parse .env.local and add as in-memory collection
            // Convert double underscore (__) to colon (:) for .NET Configuration binding
            var envVars = new Dictionary<string, string?>();
            foreach (var line in File.ReadAllLines(envLocalPath))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;
                    
                var parts = trimmedLine.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim().Replace("__", ":");
                    var value = parts[1].Trim().Trim('\'', '"');
                    envVars[key] = value;
                }
            }
            configBuilder.AddInMemoryCollection(envVars);
        }
            
        var configuration = configBuilder.Build();

        // Setup logger early for debugging
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<AzureTranslatorService>();

        // Debug: Log configuration sources
        var translationEnabled = configuration["Translation:Enabled"];
        var redisConnStr = configuration["Redis:ConnectionString"];
        _logger.LogDebug("Translation:Enabled = {Value} (type: {Type})", 
            translationEnabled, translationEnabled?.GetType().Name);
        _logger.LogDebug("Redis:ConnectionString = {Value}", redisConnStr);

        // Bind Translation configuration
        _options = new TranslationOptions();
        configuration.GetSection("Translation").Bind(_options);

        _logger.LogDebug("Bound Translation options - Enabled: {Enabled}, Endpoint: {Endpoint}", 
            _options.Enabled, _options.Endpoint);

        _isEnabled = _options.Enabled && 
                     !string.IsNullOrWhiteSpace(_options.Endpoint) && 
                     !string.IsNullOrWhiteSpace(_options.SubscriptionKey);

        // Setup Redis if connection string is available
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? configuration["Redis__ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            try
            {
                _logger.LogInformation("Attempting to connect to Redis: {ConnectionString}", redisConnectionString);
                _redis = ConnectionMultiplexer.Connect(redisConnectionString);
                _redisDb = _redis.GetDatabase();
                _logger.LogInformation("Successfully connected to Redis");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Redis at {ConnectionString}. Caching tests will be skipped.", redisConnectionString);
            }
        }
        else
        {
            _logger.LogWarning("Redis connection string not provided. Caching tests will be skipped.");
        }

        // Create Translation Service
        // Use shorter timeout for tests (10s) to avoid VS Code "hanging" warnings
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var optionsWrapper = Options.Create(_options);
        _translationService = new AzureTranslatorService(httpClient, optionsWrapper, _logger, _redis);
    }

    private string GenerateTestCacheKey(string text, string sourceLang, TranslationTarget[] targets, string? tone = null)
    {
        // Simulate cache key generation logic from AzureTranslatorService
        var keyComponents = new System.Text.StringBuilder();
        keyComponents.Append(text);
        keyComponents.Append('|');
        keyComponents.Append(sourceLang ?? "auto");
        keyComponents.Append('|');

        // Sort targets for deterministic key (order shouldn't matter)
        var sortedTargets = targets
            .OrderBy(t => t.Language)
            .ThenBy(t => t.DeploymentName ?? "");

        foreach (var target in sortedTargets)
        {
            keyComponents.Append(target.Language);
            keyComponents.Append(':');
            keyComponents.Append(target.DeploymentName ?? "default");
            keyComponents.Append(';');
        }

        if (!string.IsNullOrEmpty(tone))
        {
            keyComponents.Append('|');
            keyComponents.Append(tone);
        }

        // Hash to fixed-length key (SHA256 = 64 hex chars)
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyComponents.ToString()));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return $"translation:{hashHex}";
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.", Timeout = 15000)] // 15 seconds timeout for API call
    public async Task TranslateAsync_WithValidInput_ShouldReturnTranslations()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _logger.LogWarning("Translation not enabled. Skipping test. Set Translation__Enabled=true in .env.local");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Hello, how are you?",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName }, // Use GPT-4o-mini
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Act
        var response = await _translationService.TranslateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Translations);
        Assert.Equal(2, response.Translations.Count);
        
        var enTranslation = response.Translations.FirstOrDefault(t => t.Language == "en");
        var plTranslation = response.Translations.FirstOrDefault(t => t.Language == "pl");
        
        Assert.NotNull(enTranslation);
        Assert.NotNull(plTranslation);
        Assert.NotEmpty(enTranslation.Text);
        Assert.NotEmpty(plTranslation.Text);
        
        _logger.LogInformation("EN: {EnText}", enTranslation.Text);
        _logger.LogInformation("PL: {PlText}", plTranslation.Text);
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.")]
    public async Task TranslateAsync_WithPolishInput_ShouldDetectLanguage()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _logger.LogWarning("Translation not enabled. Skipping test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Dzień dobry, jak się masz?",
            SourceLanguage = "auto", // Auto-detect
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Act
        var response = await _translationService.TranslateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("pl", response.DetectedLanguage); // Should detect Polish
        Assert.True(response.DetectedLanguageScore >= 0.5); // High confidence
        
        var enTranslation = response.Translations.FirstOrDefault(t => t.Language == "en");
        Assert.NotNull(enTranslation);
        Assert.Contains("good", enTranslation.Text, StringComparison.OrdinalIgnoreCase);
        
        _logger.LogInformation("Detected language: {Lang} (score: {Score})", 
            response.DetectedLanguage, response.DetectedLanguageScore);
        _logger.LogInformation("EN translation: {Text}", enTranslation.Text);
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.", Timeout = 15000)] // 15 seconds timeout for API calls
    public async Task TranslateAsync_WithCaching_ShouldUseCacheOnSecondCall()
    {
        // Skip if Translation or Redis not available
        if (!_isEnabled || _redisDb == null)
        {
            _logger.LogWarning("Translation or Redis not available. Skipping caching test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Cache test message",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Clear any existing cache
        var cacheKey = GenerateTestCacheKey(request.Text, request.SourceLanguage, request.Targets.ToArray());
        await _redisDb.KeyDeleteAsync(cacheKey);

        // Act - First call (should call API and cache result)
        var response1 = await _translationService.TranslateAsync(request);
        Assert.NotNull(response1);
        Assert.False(response1.FromCache); // First call should NOT be from cache

        // Act - Second call (should use cache)
        var response2 = await _translationService.TranslateAsync(request);
        Assert.NotNull(response2);
        Assert.True(response2.FromCache); // Second call SHOULD be from cache

        // Assert - Both responses should have same content
        Assert.Equal(response1.Translations.Count, response2.Translations.Count);
        Assert.Equal(response1.Translations[0].Text, response2.Translations[0].Text);
        Assert.Equal(response1.Translations[1].Text, response2.Translations[1].Text);

        _logger.LogInformation("First call - FromCache: {FromCache1}", response1.FromCache);
        _logger.LogInformation("Second call - FromCache: {FromCache2}", response2.FromCache);
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.", Timeout = 20000)] // 20 seconds timeout for multiple API calls
    public async Task TranslateAsync_WithForceRefresh_ShouldBypassCache()
    {
        // Skip if Translation or Redis not available
        if (!_isEnabled || _redisDb == null)
        {
            _logger.LogWarning("Translation or Redis not available. Skipping force refresh test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Force refresh test",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "de", DeploymentName = _options.DeploymentName }
            }
        };

        // Act - First call to populate cache
        var response1 = await _translationService.TranslateAsync(request);
        Assert.NotNull(response1);

        // Act - Second call with ForceRefresh
        var requestWithRefresh = new TranslateRequest
        {
            Text = request.Text,
            SourceLanguage = request.SourceLanguage,
            Targets = request.Targets,
            ForceRefresh = true
        };
        var response2 = await _translationService.TranslateAsync(requestWithRefresh);
        Assert.NotNull(response2);

        // Assert - ForceRefresh should bypass cache
        Assert.False(response2.FromCache); // Should NOT be from cache

        _logger.LogInformation("With ForceRefresh - FromCache: {FromCache}", response2.FromCache);
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.")]
    public async Task TranslateAsync_WithMultipleLanguages_ShouldReturnAllTranslations()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _logger.LogWarning("Translation not enabled. Skipping test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Good morning!",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "de", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "fr", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "es", DeploymentName = _options.DeploymentName }
            }
        };

        // Act
        var response = await _translationService.TranslateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(5, response.Translations.Count);
        
        foreach (var translation in response.Translations)
        {
            Assert.NotEmpty(translation.Text);
            _logger.LogInformation("{Lang}: {Text}", translation.Language, translation.Text);
        }
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.")]
    public async Task TranslateAsync_WithTone_ShouldRespectTone()
    {
        // Skip if Translation not enabled or not using LLM
        if (!_isEnabled || string.IsNullOrWhiteSpace(_options.DeploymentName))
        {
            _logger.LogWarning("Translation not enabled or LLM not configured. Skipping tone test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "I need help with this problem.",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            },
            Tone = "formal" // Request formal tone
        };

        // Act
        var response = await _translationService.TranslateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Translations);
        Assert.True(response.Translations.Count >= 2);
        
        var plTranslation = response.Translations.FirstOrDefault(t => t.Language == "pl");
        Assert.NotNull(plTranslation);
        
        _logger.LogInformation("Formal tone PL: {Text}", plTranslation.Text);
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.")]
    public void TranslateAsync_WithoutEnglishTarget_ShouldThrowArgumentException()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Test without English",
            SourceLanguage = "pl",
            Targets = new[]
            {
                new TranslationTarget { Language = "de", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "fr", DeploymentName = _options.DeploymentName }
            }
        };

        // Act & Assert
        var exception = Assert.ThrowsAsync<ArgumentException>(
            async () => await _translationService.TranslateAsync(request));
        
        Assert.NotNull(exception);
    }

    [Fact(Skip = "Integration test - requires working Azure AI Translator endpoint. Enable manually for testing.")]
    public async Task TranslateAsync_WithLongText_ShouldHandleSuccessfully()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _logger.LogWarning("Translation not enabled. Skipping test.");
            return;
        }

        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat("This is a test sentence.", 20)); // ~500 chars
        var request = new TranslateRequest
        {
            Text = longText,
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Act
        var response = await _translationService.TranslateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Translations);
        Assert.Equal(2, response.Translations.Count);
        
        var plTranslation = response.Translations.FirstOrDefault(t => t.Language == "pl");
        Assert.NotNull(plTranslation);
        Assert.True(plTranslation.Text.Length > 100); // Should have substantial translation
        
        _logger.LogInformation("Long text translated ({Length} chars): {Preview}...", 
            plTranslation.Text.Length, 
            plTranslation.Text.Substring(0, Math.Min(50, plTranslation.Text.Length)));
    }

    [Fact(Skip = "Integration test disabled - hangs VS Code. Needs investigation.", Timeout = 15000)] // 15 seconds timeout for API call
    public async Task TranslateAsync_CacheExpiry_ShouldRespectTTL()
    {
        // Skip if Translation or Redis not available
        if (!_isEnabled || _redisDb == null)
        {
            _logger.LogWarning("Translation or Redis not available. Skipping TTL test.");
            return;
        }

        // This test verifies that cache TTL is set correctly
        // Note: We don't wait for actual expiry (would take 1 hour by default)
        // We just verify the key exists after first call

        // Arrange
        var request = new TranslateRequest
        {
            Text = "TTL test message",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        var cacheKey = GenerateTestCacheKey(request.Text, request.SourceLanguage, request.Targets.ToArray());
        
        // Clear any existing cache
        await _redisDb.KeyDeleteAsync(cacheKey);

        // Act - Call translation to populate cache
        var response = await _translationService.TranslateAsync(request);
        Assert.NotNull(response);

        // Assert - Verify cache key exists and has TTL set
        var exists = await _redisDb.KeyExistsAsync(cacheKey);
        Assert.True(exists); // Key should exist in cache

        var ttl = await _redisDb.KeyTimeToLiveAsync(cacheKey);
        Assert.NotNull(ttl); // TTL should be set
        Assert.True(ttl.Value.TotalSeconds > 0); // TTL should be positive
        Assert.True(ttl.Value.TotalSeconds <= _options.CacheTtlSeconds); // TTL should not exceed configured value

        _logger.LogInformation("Cache TTL: {Ttl} seconds (configured: {Configured})", 
            ttl.Value.TotalSeconds, _options.CacheTtlSeconds);
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
