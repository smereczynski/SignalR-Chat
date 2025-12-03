using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

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
/// Tests are designed to be non-destructive and use test-specific messages.
/// 
/// These tests are excluded from CI and are intended for local development only.
/// 
/// IMPROVEMENTS (vs original):
/// - Fixed HttpClient disposal issue (was causing hangs)
/// - Added proper CancellationToken support with explicit timeouts
/// - Improved Redis connection handling with timeout
/// - Better error messages and test output
/// - Fixed async void test method (TranslateAsync_WithoutEnglishTarget)
/// - Proper IAsyncLifetime for async initialization
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LocalOnly")]
public class TranslationServiceIntegrationTests : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output;
    private ITranslationService? _translationService;
    private IConnectionMultiplexer? _redis;
    private IDatabase? _redisDb;
    private TranslationOptions? _options;
    private bool _isEnabled;
    private ILogger<AzureTranslatorService>? _logger;
    private HttpClient? _httpClient;
    private ILoggerFactory? _loggerFactory;

    public TranslationServiceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Load configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddEnvironmentVariables();
        
        // Try to load .env.local from workspace root
        var workspaceRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        var envLocalPath = Path.Combine(workspaceRoot, ".env.local");
        
        if (File.Exists(envLocalPath))
        {
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

        // Setup logger that writes to test output
        _loggerFactory = LoggerFactory.Create(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        _logger = _loggerFactory.CreateLogger<AzureTranslatorService>();

        // Bind Translation configuration
        _options = new TranslationOptions();
        configuration.GetSection("Translation").Bind(_options);

        _output.WriteLine($"Translation Enabled: {_options.Enabled}");
        _output.WriteLine($"Translation Endpoint: {_options.Endpoint}");

        _isEnabled = _options.Enabled && 
                     !string.IsNullOrWhiteSpace(_options.Endpoint) && 
                     !string.IsNullOrWhiteSpace(_options.SubscriptionKey);

        if (!_isEnabled)
        {
            _output.WriteLine("Translation service not enabled or not configured. Tests will be skipped.");
            return;
        }

        // Setup Redis with connection timeout
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? configuration["Redis__ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            try
            {
                _output.WriteLine($"Connecting to Redis: {redisConnectionString}");
                
                // Add connection timeout to avoid hangs
                var configOptions = ConfigurationOptions.Parse(redisConnectionString);
                configOptions.ConnectTimeout = 3000; // 3 seconds
                configOptions.SyncTimeout = 3000;
                configOptions.AbortOnConnectFail = false; // Don't fail tests if Redis unavailable
                
                _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
                _redisDb = _redis.GetDatabase();
                _output.WriteLine("Successfully connected to Redis");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to connect to Redis: {ex.Message}");
                _output.WriteLine("Caching tests will be skipped but API tests will run.");
            }
        }

        // Create HttpClient with proper timeout (will be disposed in Dispose)
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30) // Reasonable timeout for API calls
        };
        
        var optionsWrapper = Options.Create(_options);
        _translationService = new AzureTranslatorService(_httpClient, optionsWrapper, _logger, _redis);
        
        _output.WriteLine("Test initialization complete");
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
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

    [Fact]
    public async Task TranslateAsync_WithValidInput_ShouldReturnTranslations()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _output.WriteLine("Translation not enabled. Set Translation__Enabled=true in .env.local");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Hello, how are you?",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Act with explicit timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await _translationService!.TranslateAsync(request, cts.Token);

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
        
        _output.WriteLine($"EN: {enTranslation.Text}");
        _output.WriteLine($"PL: {plTranslation.Text}");
        _output.WriteLine($"From cache: {response.FromCache}");
    }

    [Fact]
    public async Task TranslateAsync_WithCaching_ShouldUseCacheOnSecondCall()
    {
        // Skip if Translation or Redis not available
        if (!_isEnabled || _redisDb == null)
        {
            _output.WriteLine("Translation or Redis not available. Skipping caching test.");
            return;
        }

        // Arrange
        var uniqueText = $"Cache test {Guid.NewGuid():N}"; // Unique text to avoid cache conflicts
        var request = new TranslateRequest
        {
            Text = uniqueText,
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Clear any existing cache
        var cacheKey = GenerateTestCacheKey(request.Text, request.SourceLanguage, request.Targets.ToArray());
        await _redisDb.KeyDeleteAsync(cacheKey);

        // Act - First call (should call API and cache result)
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response1 = await _translationService!.TranslateAsync(request, cts1.Token);
        Assert.NotNull(response1);
        Assert.False(response1.FromCache); // First call should NOT be from cache
        _output.WriteLine($"First call - FromCache: {response1.FromCache}");

        // Small delay to ensure cache write completes
        await Task.Delay(100);

        // Act - Second call (should use cache)
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response2 = await _translationService.TranslateAsync(request, cts2.Token);
        Assert.NotNull(response2);
        Assert.True(response2.FromCache); // Second call SHOULD be from cache
        _output.WriteLine($"Second call - FromCache: {response2.FromCache}");

        // Assert - Both responses should have same content
        Assert.Equal(response1.Translations.Count, response2.Translations.Count);
        Assert.Equal(response1.Translations[0].Text, response2.Translations[0].Text);
        Assert.Equal(response1.Translations[1].Text, response2.Translations[1].Text);

        // Cleanup
        await _redisDb.KeyDeleteAsync(cacheKey);
    }

    [Fact]
    public async Task TranslateAsync_WithForceRefresh_ShouldBypassCache()
    {
        // Skip if Translation or Redis not available
        if (!_isEnabled || _redisDb == null)
        {
            _output.WriteLine("Translation or Redis not available. Skipping force refresh test.");
            return;
        }

        // Arrange
        var uniqueText = $"Force refresh test {Guid.NewGuid():N}";
        var request = new TranslateRequest
        {
            Text = uniqueText,
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "de", DeploymentName = _options.DeploymentName }
            }
        };

        // Act - First call to populate cache
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response1 = await _translationService!.TranslateAsync(request, cts1.Token);
        Assert.NotNull(response1);
        _output.WriteLine($"First call - FromCache: {response1.FromCache}");

        // Small delay to ensure cache write completes
        await Task.Delay(100);

        // Act - Second call with ForceRefresh
        var requestWithRefresh = new TranslateRequest
        {
            Text = request.Text,
            SourceLanguage = request.SourceLanguage,
            Targets = request.Targets,
            ForceRefresh = true
        };
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response2 = await _translationService.TranslateAsync(requestWithRefresh, cts2.Token);
        Assert.NotNull(response2);

        // Assert - ForceRefresh should bypass cache
        Assert.False(response2.FromCache); // Should NOT be from cache
        _output.WriteLine($"With ForceRefresh - FromCache: {response2.FromCache}");

        // Cleanup
        var cacheKey = GenerateTestCacheKey(request.Text, request.SourceLanguage, request.Targets.ToArray());
        await _redisDb.KeyDeleteAsync(cacheKey);
    }

    [Fact]
    public async Task TranslateAsync_WithMultipleLanguages_ShouldReturnAllTranslations()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _output.WriteLine("Translation not enabled. Skipping test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Good morning!",
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "de", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "fr", DeploymentName = _options.DeploymentName },
                new TranslationTarget { Language = "es", DeploymentName = _options.DeploymentName }
            }
        };

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)); // Longer timeout for multiple languages
        var response = await _translationService!.TranslateAsync(request, cts.Token);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(5, response.Translations.Count);
        
        foreach (var translation in response.Translations)
        {
            Assert.NotEmpty(translation.Text);
            _output.WriteLine($"{translation.Language}: {translation.Text}");
        }
    }

    [Fact]
    public async Task TranslateAsync_WithTone_ShouldRespectTone()
    {
        // Skip if Translation not enabled or not using LLM
        if (!_isEnabled || string.IsNullOrWhiteSpace(_options!.DeploymentName))
        {
            _output.WriteLine("Translation not enabled or LLM not configured. Skipping tone test.");
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await _translationService!.TranslateAsync(request, cts.Token);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Translations);
        Assert.True(response.Translations.Count >= 2);
        
        var plTranslation = response.Translations.FirstOrDefault(t => t.Language == "pl");
        Assert.NotNull(plTranslation);
        
        _output.WriteLine($"Formal tone PL: {plTranslation.Text}");
    }

    [Fact]
    public async Task TranslateAsync_WithoutEnglishTarget_ShouldThrowArgumentException()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _output.WriteLine("Translation not enabled. Skipping test.");
            return;
        }

        // Arrange
        var request = new TranslateRequest
        {
            Text = "Test without English",
            SourceLanguage = "pl",
            Targets = new[]
            {
                new TranslationTarget { Language = "de", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "fr", DeploymentName = _options.DeploymentName }
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _translationService!.TranslateAsync(request));
        
        Assert.NotNull(exception);
        Assert.Contains("English", exception.Message);
        _output.WriteLine($"Exception correctly thrown: {exception.Message}");
    }

    [Fact]
    public async Task TranslateAsync_WithLongText_ShouldHandleSuccessfully()
    {
        // Skip if Translation not enabled
        if (!_isEnabled)
        {
            _output.WriteLine("Translation not enabled. Skipping test.");
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
                new TranslationTarget { Language = "en", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)); // Longer timeout for large text
        var response = await _translationService!.TranslateAsync(request, cts.Token);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Translations);
        Assert.Equal(2, response.Translations.Count);
        
        var plTranslation = response.Translations.FirstOrDefault(t => t.Language == "pl");
        Assert.NotNull(plTranslation);
        Assert.True(plTranslation.Text.Length > 100); // Should have substantial translation
        
        _output.WriteLine($"Long text translated ({plTranslation.Text.Length} chars): {plTranslation.Text.Substring(0, Math.Min(50, plTranslation.Text.Length))}...");
    }

    [Fact]
    public async Task TranslateAsync_CacheExpiry_ShouldRespectTTL()
    {
        // Skip if Translation or Redis not available
        if (!_isEnabled || _redisDb == null)
        {
            _output.WriteLine("Translation or Redis not available. Skipping TTL test.");
            return;
        }

        // This test verifies that cache TTL is set correctly
        // Note: We don't wait for actual expiry (would take 1 hour by default)
        // We just verify the key exists after first call

        // Arrange
        var uniqueText = $"TTL test {Guid.NewGuid():N}";
        var request = new TranslateRequest
        {
            Text = uniqueText,
            SourceLanguage = "en",
            Targets = new[]
            {
                new TranslationTarget { Language = "en", DeploymentName = _options!.DeploymentName },
                new TranslationTarget { Language = "pl", DeploymentName = _options.DeploymentName }
            }
        };

        var cacheKey = GenerateTestCacheKey(request.Text, request.SourceLanguage, request.Targets.ToArray());
        
        // Clear any existing cache
        await _redisDb.KeyDeleteAsync(cacheKey);

        // Act - Call translation to populate cache
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await _translationService!.TranslateAsync(request, cts.Token);
        Assert.NotNull(response);

        // Small delay to ensure cache write completes
        await Task.Delay(100);

        // Assert - Verify cache key exists and has TTL set
        var exists = await _redisDb.KeyExistsAsync(cacheKey);
        Assert.True(exists); // Key should exist in cache

        var ttl = await _redisDb.KeyTimeToLiveAsync(cacheKey);
        Assert.NotNull(ttl); // TTL should be set
        Assert.True(ttl.Value.TotalSeconds > 0); // TTL should be positive
        Assert.True(ttl.Value.TotalSeconds <= _options.CacheTtlSeconds); // TTL should not exceed configured value

        _output.WriteLine($"Cache TTL: {ttl.Value.TotalSeconds:F0} seconds (configured: {_options.CacheTtlSeconds})");

        // Cleanup
        await _redisDb.KeyDeleteAsync(cacheKey);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _redis?.Dispose();
        _loggerFactory?.Dispose();
    }
}
