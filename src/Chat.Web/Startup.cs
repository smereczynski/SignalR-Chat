using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Chat.Web.Hubs;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Authorization;
using StackExchange.Redis;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using Chat.Web.Observability;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.AspNetCore;
using Chat.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;
// OpenTelemetry instrumentation extension namespaces (ensure packages installed: OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Exporter.OpenTelemetryProtocol)
using System.Diagnostics;
#if OPENTELEMETRY_INSTRUMENTATION
using OpenTelemetry.Instrumentation.AspNetCore;
#endif
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Azure.Monitor.OpenTelemetry.Exporter;
using System.Reflection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using System.Diagnostics.Metrics;
using OpenTelemetry.Instrumentation.Runtime;
using OpenTelemetry.Instrumentation.StackExchangeRedis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Chat.Web.Middleware;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Chat.Web
{
    /// <summary>
    /// Configures dependency injection and the HTTP request pipeline for the chat application.
    /// Responsibilities:
    ///  - Register persistence (Cosmos / InMemory) and OTP (Redis / InMemory)
    ///  - Configure authentication (cookie vs test header auth) & SignalR (Azure vs in-process)
    ///  - Set up OpenTelemetry (Traces + Metrics + Logs) with exporter auto-selection
    ///  - Apply rate limiting to sensitive OTP endpoints
    ///  - Expose health and hub endpoints
    /// </summary>
    public class Startup : IDisposable
    {
        /// <summary>
        /// Static holder for custom domain meters and counters. These are added to the MeterProvider in <see cref="ConfigureServices"/>.
        /// Keeping them nested avoids accidental public exposure while still enabling reuse in controllers/hubs.
        /// </summary>
        private static class Metrics
        {
            public const string InstrumentationName = "Chat.Web";
            public static readonly Meter Meter = new Meter(InstrumentationName, "1.0.0");
            public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>("chat.messages.sent");
            public static readonly Counter<long> RoomsJoined = Meter.CreateCounter<long>("chat.rooms.joined");
            public static readonly Counter<long> OtpRequests = Meter.CreateCounter<long>("chat.otp.requests");
            public static readonly Counter<long> OtpVerifications = Meter.CreateCounter<long>("chat.otp.verifications");
            public static readonly Counter<long> ReconnectAttempts = Meter.CreateCounter<long>("chat.reconnect.attempts");
        }

    /// <summary>
    /// Chooses a trace exporter based on configuration priority (Azure Monitor > OTLP > Console).
    /// </summary>
    private static void AddSelectedExporter(TracerProviderBuilder builder, string otlpEndpoint, IConfiguration config)
        {
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var aiConn = config["ApplicationInsights:ConnectionString"] ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            if (!string.IsNullOrWhiteSpace(aiConn) && string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddAzureMonitorTraceExporter(o => 
                {
                    o.ConnectionString = aiConn;
                    // Enable adaptive sampling: Application Insights automatically adjusts sampling rate
                    // based on telemetry volume (typically 5-10 items/sec per type)
                    // Errors and exceptions are never sampled
                });
            }
            else if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                // Use HTTP/Protobuf when targeting the collector on port 4318; otherwise default to gRPC
                var endpointUri = new Uri(otlpEndpoint);
                var protocol = otlpEndpoint.Contains(":4318") ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
                builder.AddOtlpExporter(o => { o.Endpoint = endpointUri; o.Protocol = protocol; });
            }
            else
            {
                builder.AddConsoleExporter();
            }
        }

    /// <summary>
    /// Chooses a metrics exporter based on configuration priority (Azure Monitor > OTLP > Console).
    /// </summary>
    private static void AddSelectedExporter(MeterProviderBuilder builder, string otlpEndpoint, IConfiguration config)
        {
            var aiConn = config["ApplicationInsights:ConnectionString"] ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            
            // Priority: Application Insights (when configured) > OTLP > Console (local only)
            if (!string.IsNullOrWhiteSpace(aiConn))
            {
                // Application Insights available (Azure or local with AI configured)
                builder.AddAzureMonitorMetricExporter(o => o.ConnectionString = aiConn);
            }
            else if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                // OTLP endpoint configured (local LGTM stack)
                var endpointUri = new Uri(otlpEndpoint);
                var protocol = otlpEndpoint.Contains(":4318") ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
                builder.AddOtlpExporter(o => { o.Endpoint = endpointUri; o.Protocol = protocol; });
            }
            else
            {
                // No Application Insights, no OTLP - use console for local development
                builder.AddConsoleExporter();
            }
        }

    /// <summary>
    /// Chooses a log exporter based on configuration priority (Azure Monitor > OTLP > Console).
    /// </summary>
    private static void AddSelectedExporter(OpenTelemetryLoggerOptions logging, string otlpEndpoint, IConfiguration config)
        {
            var aiConn = config["ApplicationInsights:ConnectionString"] ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            
            // Priority: Application Insights (when configured) > OTLP > Console (local only)
            if (!string.IsNullOrWhiteSpace(aiConn))
            {
                // Application Insights available (Azure or local with AI configured)
                logging.AddAzureMonitorLogExporter(o => o.ConnectionString = aiConn);
            }
            else if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                // OTLP endpoint configured (local LGTM stack)
                var endpointUri = new Uri(otlpEndpoint);
                var protocol = otlpEndpoint.Contains(":4318") ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
                logging.AddOtlpExporter(o => { o.Endpoint = endpointUri; o.Protocol = protocol; });
            }
            else
            {
                // No Application Insights, no OTLP - use console for local development
                logging.AddConsoleExporter();
            }
        }
        /// <summary>
        /// Constructs the startup instance (configuration injected by host).
        /// </summary>
        private ILoggerFactory _loggerFactory;
        private ILogger<Startup> _logger;
        private bool _disposed = false;

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            HostEnvironment = environment;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment HostEnvironment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
    /// <summary>
    /// Registers application services and infrastructure components.
    /// Conditional logic toggles between production cloud services and in-memory test doubles.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
        {
            // Create early logger for startup diagnostics
            _loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: false));
            _logger = _loggerFactory.CreateLogger<Startup>();

            var inMemoryTest = string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase);
            // Defer OpenTelemetry registration until after external clients (Cosmos, Redis) are registered
            var otlpEndpoint = Configuration["OTel:OtlpEndpoint"]; // e.g. http://localhost:4317 or https://otlp.yourdomain:4317
            var assemblyVersion = typeof(Startup).Assembly.GetName().Version?.ToString() ?? "unknown";

            // OpenTelemetry logging provider (simple; Serilog remains primary)
            // Logs (AppTraces table) are NOT sampled - all application logs are captured
            // Traces (traces table) are sampled at 20% for cost optimization (configured below in WithTracing)
            services.AddLogging(lb =>
            {
                lb.AddOpenTelemetry(o =>
                {
                    AddSelectedExporter(o, otlpEndpoint, Configuration);
                    o.IncludeFormattedMessage = true;
                    o.IncludeScopes = false;
                    // Respect appsettings.json LogLevel configuration
                    o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(Tracing.ServiceName));
                });
            });
            // Options
            services.Configure<CosmosOptions>(Configuration.GetSection("Cosmos"));
            services.Configure<RedisOptions>(Configuration.GetSection("Redis"));
            services.Configure<AcsOptions>(Configuration.GetSection("Acs"));
            services.Configure<OtpOptions>(Configuration.GetSection("Otp"));
            services.Configure<EntraIdOptions>(Configuration.GetSection("EntraId"));
            services.Configure<Chat.Web.Options.NotificationOptions>(Configuration.GetSection("Notifications"));
            services.Configure<Chat.Web.Options.RateLimitingOptions>(Configuration.GetSection("RateLimiting:MarkRead"));
            services.Configure<Chat.Web.Options.TranslationOptions>(Configuration.GetSection("Translation"));
            services.PostConfigure<OtpOptions>(opts =>
            {
                // Allow env var override of pepper per guide: Otp__Pepper
                var envPepper = Environment.GetEnvironmentVariable("Otp__Pepper");
                if (!string.IsNullOrWhiteSpace(envPepper)) opts.Pepper = envPepper;
            });
            services.PostConfigure<EntraIdOptions>(opts =>
            {
                // Load ClientId and ClientSecret from connection string if not already configured
                if (string.IsNullOrWhiteSpace(opts.ClientSecret))
                {
                    var connectionString = Configuration.GetConnectionString("EntraId");
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        // Parse connection string format: "ClientId=<id>;ClientSecret=<secret>"
                        var parts = connectionString.Split(';');
                        foreach (var part in parts)
                        {
                            var keyValue = part.Split('=', 2);
                            if (keyValue.Length == 2)
                            {
                                if (keyValue[0].Trim().Equals("ClientId", StringComparison.OrdinalIgnoreCase) &&
                                    string.IsNullOrWhiteSpace(opts.ClientId))
                                {
                                    opts.ClientId = keyValue[1].Trim();
                                }
                                else if (keyValue[0].Trim().Equals("ClientSecret", StringComparison.OrdinalIgnoreCase))
                                {
                                    opts.ClientSecret = keyValue[1].Trim();
                                }
                            }
                        }
                    }
                }
            });

            services.PostConfigure<Chat.Web.Options.TranslationOptions>(opts =>
            {
                // In Azure App Service, secrets are sometimes configured under "Connection strings".
                // Bicep currently sets Translation__SubscriptionKey as a connection string, while the app binds
                // TranslationOptions from the "Translation" section. Bridge that gap here.
                if (string.IsNullOrWhiteSpace(opts.SubscriptionKey))
                {
                    static string GetEnvVarInsensitive(string name)
                    {
                        var direct = Environment.GetEnvironmentVariable(name);
                        if (!string.IsNullOrWhiteSpace(direct))
                        {
                            return direct;
                        }

                        // Azure App Service on Linux has been observed to uppercase environment variable names.
                        // Environment.GetEnvironmentVariable is case-sensitive on Linux, so probe case-insensitively.
                        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                        {
                            if (entry.Key is string key && key.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                return entry.Value as string;
                            }
                        }

                        return null;
                    }

                    // NOTE: On Azure App Service, entries from the "Connection strings" blade are injected as
                    // environment variables with provider-specific prefixes (e.g. CUSTOMCONNSTR_{name}).
                    // These are not always surfaced via IConfiguration as ConnectionStrings:{name} unless an
                    // additional provider is used. Handle the common App Service prefixes explicitly.
                    var subscriptionKey = Configuration.GetConnectionString("Translation__SubscriptionKey")
                        ?? GetEnvVarInsensitive("CUSTOMCONNSTR_Translation__SubscriptionKey")
                        ?? GetEnvVarInsensitive("ConnectionStrings__Translation__SubscriptionKey")
                        ?? GetEnvVarInsensitive("Translation__SubscriptionKey")
                        ?? GetEnvVarInsensitive("Translation:SubscriptionKey");
                    if (!string.IsNullOrWhiteSpace(subscriptionKey))
                    {
                        opts.SubscriptionKey = subscriptionKey;
                    }
                }
            });
            services.AddSingleton<IOtpHasher, Argon2OtpHasher>();

            // Baseline health checks so mapping exists even in Testing:InMemory mode
            services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());

            var inMemory = Configuration["Testing:InMemory"];
            if (string.Equals(inMemory, "true", StringComparison.OrdinalIgnoreCase))
            {
                // Lightweight in-memory repositories for integration tests (no external services)
                services.AddSingleton<IUsersRepository, InMemoryUsersRepository>();
                services.AddSingleton<IRoomsRepository, InMemoryRoomsRepository>();
                services.AddSingleton<IMessagesRepository, InMemoryMessagesRepository>();
                services.AddSingleton<IOtpStore, InMemoryOtpStore>();
                services.AddSingleton<Services.IPresenceTracker, Services.InMemoryPresenceTracker>();
            }
            else
            {
                // Cosmos required (fail fast if placeholder)
                // Azure App Service injects connection strings as CUSTOMCONNSTR_{name}
                // Configuration binding automatically handles both formats
                var cosmosConn = Configuration.GetConnectionString("Cosmos") 
                    ?? Configuration["Cosmos:ConnectionString"];
                cosmosConn = ConfigurationGuards.Require(cosmosConn, "Cosmos connection string");
                
                var cosmosOpts = new CosmosOptions
                {
                    ConnectionString = cosmosConn,
                    Database = Configuration["Cosmos:Database"] ?? "chat",
                    MessagesContainer = Configuration["Cosmos:MessagesContainer"] ?? "messages",
                    UsersContainer = Configuration["Cosmos:UsersContainer"] ?? "users",
                    RoomsContainer = Configuration["Cosmos:RoomsContainer"] ?? "rooms",
                };
                // Configure messages TTL: set to a number (seconds), -1 to enable TTL with no expiry, or null/empty to disable TTL entirely
                var ttlRaw = Configuration["Cosmos:MessagesTtlSeconds"];
                if (string.Equals(ttlRaw, "null", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(ttlRaw))
                {
                    cosmosOpts.MessagesTtlSeconds = null; // disable TTL
                }
                else if (int.TryParse(ttlRaw, out var ttlParsed))
                {
                    cosmosOpts.MessagesTtlSeconds = ttlParsed;
                }
                // Initialize Cosmos DB clients using hosted service to avoid blocking during DI registration
                // Store options for deferred initialization
                services.AddSingleton(cosmosOpts);
                
                // Register CosmosClients as a placeholder that will be set by the initialization service
                CosmosClients cosmosClientsInstance = null;
                services.AddSingleton(sp => cosmosClientsInstance ?? throw new InvalidOperationException("CosmosClients not yet initialized. Ensure CosmosClientsInitializationService has started."));
                
                // Register initialization service that will run async initialization properly
                services.AddHostedService(sp => new Services.CosmosClientsInitializationService(
                    cosmosOpts,
                    sp.GetRequiredService<ILogger<Services.CosmosClientsInitializationService>>(),
                    clients => cosmosClientsInstance = clients
                ));
                services.AddSingleton<IUsersRepository, CosmosUsersRepository>();
                services.AddSingleton<IRoomsRepository, CosmosRoomsRepository>();
                services.AddSingleton<IMessagesRepository, CosmosMessagesRepository>();

                // Data seeder service (seeds initial data in background if database is empty)
                services.AddHostedService<Services.DataSeederService>();
                
                // Translation background service (processes queued translation jobs)
                var translationOptions = Configuration.GetSection("Translation").Get<Options.TranslationOptions>();
                if (translationOptions?.Enabled == true)
                {
                    services.AddHostedService<Services.TranslationBackgroundService>();
                }

                // Redis OTP store (fail fast if placeholder)
                // Azure App Service injects connection strings as CUSTOMCONNSTR_{name}
                // Configuration binding automatically handles both formats
                var redisConn = Configuration.GetConnectionString("Redis") 
                    ?? Configuration["Redis:ConnectionString"];
                redisConn = ConfigurationGuards.Require(redisConn, "Redis connection string");
                
                // Configure Redis with shorter timeouts for multi-instance scenarios and logging
                var redisConfig = ConfigurationOptions.Parse(redisConn);
                redisConfig.ConnectTimeout = 5000; // 5 seconds
                redisConfig.SyncTimeout = 5000;
                redisConfig.AbortOnConnectFail = false; // Don't fail startup if Redis is temporarily unavailable
                
                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<Startup>>();
                    try
                    {
                        logger.LogInformation("Connecting to Redis at {Endpoints} (SSL: {UseSsl}, Timeout: {ConnectTimeout}ms)",
                            string.Join(", ", redisConfig.EndPoints.Select(ep => ep.ToString())),
                            redisConfig.Ssl,
                            redisConfig.ConnectTimeout);
                        
                        var mux = ConnectionMultiplexer.Connect(redisConfig);
                        
                        // Log connection events
                        mux.ConnectionFailed += (sender, args) =>
                            logger.LogError("Redis connection failed: {EndPoint}, FailureType: {FailureType}, Exception: {Exception}",
                                args.EndPoint, args.FailureType, args.Exception?.Message);
                        
                        mux.ConnectionRestored += (sender, args) =>
                            logger.LogInformation("Redis connection restored: {EndPoint}", args.EndPoint);
                        
                        mux.ErrorMessage += (sender, args) =>
                            logger.LogError("Redis error message: {EndPoint}, Message: {Message}", args.EndPoint, args.Message);
                        
                        mux.InternalError += (sender, args) =>
                            logger.LogError(args.Exception, "Redis internal error: {EndPoint}", args.EndPoint);
                        
                        logger.LogInformation("Successfully connected to Redis. Status: {Status}, Endpoints: {Endpoints}",
                            mux.IsConnected ? "Connected" : "Disconnected",
                            string.Join(", ", mux.GetEndPoints().Select(ep => ep.ToString())));
                        
                        return mux;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to connect to Redis. Endpoints: {Endpoints}, SSL: {UseSsl}",
                            string.Join(", ", redisConfig.EndPoints.Select(ep => ep.ToString())),
                            redisConfig.Ssl);
                        throw;
                    }
                });
                services.AddSingleton<IOtpStore, RedisOtpStore>();
                services.AddSingleton<Services.IPresenceTracker, Services.RedisPresenceTracker>();

                // Note: UnreadNotificationScheduler disabled - requires refactoring for lazy CosmosClients resolution
                
                // Presence cleanup service
                services.AddHostedService<Services.PresenceCleanupService>();

                // Health checks for Redis and Cosmos with timeouts
                services.AddHealthChecks()
                    .AddCheck<Chat.Web.Health.RedisHealthCheck>("redis", tags: new[] { "ready" }, timeout: TimeSpan.FromSeconds(3))
                    .AddCheck<Chat.Web.Health.CosmosHealthCheck>("cosmos", tags: new[] { "ready" }, timeout: TimeSpan.FromSeconds(5));
                
                // Register health check publisher for Application Insights logging
                services.AddSingleton<IHealthCheckPublisher, Chat.Web.Health.ApplicationInsightsHealthCheckPublisher>();
                services.Configure<HealthCheckPublisherOptions>(options =>
                {
                    options.Delay = TimeSpan.FromSeconds(2); // Initial delay before first publish
                    options.Period = TimeSpan.FromSeconds(30); // Publish results every 30 seconds
                });
            }
            
            // OTP sender: always use console mock in test mode, otherwise prefer ACS if configured
            if (inMemoryTest)
            {
                services.AddSingleton<IOtpSender, ConsoleOtpSender>();
            }
            else
            {
                // Azure App Service injects connection strings as CUSTOMCONNSTR_{name}
                // Configuration binding automatically handles both formats
                var acsConn = Configuration.GetConnectionString("ACS") 
                    ?? Configuration["Acs:ConnectionString"];
                    
                if (!string.IsNullOrWhiteSpace(acsConn))
                {
                    var acsOptions = new AcsOptions
                    {
                        ConnectionString = acsConn,
                        EmailFrom = Configuration["Acs:EmailFrom"],
                        SmsFrom = Configuration["Acs:SmsFrom"]
                    };
                    services.AddSingleton<IOtpSender>(sp => new AcsOtpSender(
                        acsOptions, 
                        sp.GetRequiredService<ILogger<AcsOtpSender>>(),
                        sp.GetRequiredService<IStringLocalizer<Resources.SharedResources>>()));
                    // Include ACS in health checks if present (config check only)
                    services.AddHealthChecks().AddCheck("acs-config", () => HealthCheckResult.Healthy("configured"), tags: new[] { "ready" });
                }
                else
                {
                    services.AddSingleton<IOtpSender, ConsoleOtpSender>();
                }
            }

            // Localization configuration
            services.AddLocalization(); // Don't set ResourcesPath - resources are in namespace path
            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[]
                {
                    new System.Globalization.CultureInfo("en"), // English (default)

                    // Include neutral cultures (e.g. "pl") so browsers sending only a language tag
                    // (without region, like "pl" instead of "pl-PL") still match.
                    new System.Globalization.CultureInfo("pl"),
                    new System.Globalization.CultureInfo("pl-PL"), // Poland

                    new System.Globalization.CultureInfo("de"),
                    new System.Globalization.CultureInfo("de-DE"), // Germany

                    new System.Globalization.CultureInfo("cs"),
                    new System.Globalization.CultureInfo("cs-CZ"), // Czech Republic

                    new System.Globalization.CultureInfo("sk"),
                    new System.Globalization.CultureInfo("sk-SK"), // Slovakia

                    new System.Globalization.CultureInfo("uk"),
                    new System.Globalization.CultureInfo("uk-UA"), // Ukraine

                    new System.Globalization.CultureInfo("lt"),
                    new System.Globalization.CultureInfo("lt-LT"), // Lithuania

                    new System.Globalization.CultureInfo("ru"),
                    new System.Globalization.CultureInfo("ru-RU")  // Russia
                };
                
                options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en");
                options.SupportedCultures = supportedCultures;
                options.SupportedUICultures = supportedCultures;
                
                // Priority: Cookie > Accept-Language > Default
                options.RequestCultureProviders = new List<Microsoft.AspNetCore.Localization.IRequestCultureProvider>
                {
                    new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider(),
                    new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider()
                };
            });

            services.AddRazorPages();
            services.AddControllers();
            
            // Register authorization handler for home tenant validation
            services.AddSingleton<IAuthorizationHandler, Authorization.HomeTenantHandler>();

            // Authorization policies
            services.AddAuthorization(options =>
            {
                // Default policy: authenticated users (any tenant)
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                
                // Admin policy: requires Admin.ReadWrite app role + home tenant
                // Tenant validation happens both in OnTokenValidated (for production)
                // and via HomeTenantRequirement (for testing and defense-in-depth)
                var entraIdOpts = new EntraIdOptions();
                Configuration.GetSection("EntraId").Bind(entraIdOpts);
                
                if (!string.IsNullOrWhiteSpace(entraIdOpts.Authorization?.AdminRoleValue))
                {
                    options.AddPolicy("RequireAdminRole", policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.RequireRole(entraIdOpts.Authorization.AdminRoleValue);
                        policy.Requirements.Add(new Authorization.HomeTenantRequirement());
                    });
                }
            });
            
            services.AddSingleton<Services.IInProcessMetrics, Services.InProcessMetrics>();
            
            // HSTS configuration for production (1-year max-age, preload, includeSubDomains)
            if (!HostEnvironment.IsDevelopment())
            {
                services.AddHsts(options =>
                {
                    options.Preload = true;
                    options.IncludeSubDomains = true;
                    options.MaxAge = TimeSpan.FromDays(365);
                    options.ExcludedHosts.Clear(); // Remove localhost exclusion
                });
            }
            // Rate limiting for hub operations
            services.AddSingleton<Services.IMarkReadRateLimiter, Services.MarkReadRateLimiter>();
            
            // Translation service (with Redis dependency for caching)
            services.AddHttpClient<Services.ITranslationService, Services.AzureTranslatorService>((sp, client) =>
            {
                // HttpClient configuration if needed (timeout, headers, etc.)
            });
            
            // Translation job queue (Redis-based)
            services.AddSingleton<Services.ITranslationJobQueue, Services.TranslationJobQueue>();
            
            // Notification plumbing
            services.AddSingleton<Services.INotificationSender, Services.NotificationSender>();
            
            // Rate limiting: protect auth endpoints (OTP request / verify) - configurable for tests vs prod
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                var permitLimit = Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 5;
                var windowSeconds = Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60; // default 1 minute
                var queueLimit = Configuration.GetValue<int?>("RateLimiting:Auth:QueueLimit") ?? 0; // default reject overflow
                options.AddPolicy("AuthEndpoints", context =>
                    RateLimitPartition.GetFixedWindowLimiter(partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anon",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = permitLimit,
                            Window = TimeSpan.FromSeconds(windowSeconds),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = queueLimit
                        }));
            });
            // ==========================================
            // Authentication Configuration
            // ==========================================
            if (string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase))
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            }
            else
            {
                // Bind EntraIdOptions manually to check if configured (PostConfigure will handle DI)
                var entraIdOptions = new EntraIdOptions();
                Configuration.GetSection("EntraId").Bind(entraIdOptions);
                
                // Check connection string for ClientId/ClientSecret
                var connectionString = Configuration.GetConnectionString("EntraId");
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    var parts = connectionString.Split(';');
                    foreach (var part in parts)
                    {
                        var keyValue = part.Split('=', 2);
                        if (keyValue.Length == 2)
                        {
                            if (keyValue[0].Trim().Equals("ClientId", StringComparison.OrdinalIgnoreCase) &&
                                string.IsNullOrWhiteSpace(entraIdOptions.ClientId))
                            {
                                entraIdOptions.ClientId = keyValue[1].Trim();
                            }
                            else if (keyValue[0].Trim().Equals("ClientSecret", StringComparison.OrdinalIgnoreCase))
                            {
                                entraIdOptions.ClientSecret = keyValue[1].Trim();
                            }
                        }
                    }
                }
                
                var authBuilder = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
                
                // Add Entra ID authentication if configured
                if (entraIdOptions.IsEnabled)
                {
                    authBuilder.AddMicrosoftIdentityWebApp(
                        microsoftIdentityOptions =>
                        {
                            microsoftIdentityOptions.Instance = entraIdOptions.Instance;
                            microsoftIdentityOptions.TenantId = entraIdOptions.TenantId;
                            microsoftIdentityOptions.ClientId = entraIdOptions.ClientId;
                            microsoftIdentityOptions.ClientSecret = entraIdOptions.ClientSecret;
                            microsoftIdentityOptions.CallbackPath = entraIdOptions.CallbackPath;
                            microsoftIdentityOptions.SignedOutCallbackPath = entraIdOptions.SignedOutCallbackPath;
                            
                            // Use authorization code flow only (no implicit/hybrid flow requiring ID tokens)
                            microsoftIdentityOptions.ResponseType = Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType.Code;
                            
                            // Use the main cookie scheme (not a separate one)
                            microsoftIdentityOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                            
                            // Token validation
                            microsoftIdentityOptions.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidateAudience = true,
                                ValidateLifetime = true,
                                ValidateIssuerSigningKey = true,
                                ClockSkew = TimeSpan.FromMinutes(5)
                            };
                            
                            // Events for custom logic
                            microsoftIdentityOptions.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                            {
                                OnRedirectToIdentityProvider = context =>
                                {
                                    // Check both Parameters and Items for silent flag
                                    var isSilentParams = context.Properties?.Parameters.TryGetValue("silent", out var silentVal) == true &&
                                                         ((silentVal as string) == "true" || (silentVal is bool b && b));
                                    var isSilentItems = context.Properties?.Items.TryGetValue("silent", out var silentItemVal) == true &&
                                                        silentItemVal == "true";
                                    var isSilent = isSilentParams || isSilentItems;
                                    
                                    if (isSilent)
                                    {
                                        // Request silent sign-in (no UI); identity provider will return login_required/interaction_required if not possible
                                        context.ProtocolMessage.Prompt = "none";
                                    }
                                    return Task.CompletedTask;
                                },
                                OnTokenValidated = async context =>
                                {
                                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
                                    logger.LogWarning("===== OnTokenValidated START =====");
                                    
                                    var usersRepo = context.HttpContext.RequestServices.GetRequiredService<IUsersRepository>();
                                    
                                    // Extract claims
                                    var upn = context.Principal?.FindFirst("preferred_username")?.Value;
                                    var tenantId = context.Principal?.FindFirst("tid")?.Value;
                                    var issuerClaim = context.Principal?.FindFirst("iss")?.Value;
                                    var email = context.Principal?.FindFirst("email")?.Value ?? upn;
                                    var displayName = context.Principal?.FindFirst("name")?.Value;
                                    var country = context.Principal?.FindFirst("country")?.Value;
                                    var region = context.Principal?.FindFirst("state")?.Value;
                                    
                                    logger.LogWarning("===== OnTokenValidated: Extracted UPN={Upn}, TenantId={TenantId} =====", upn ?? "<null>", tenantId ?? "<null>");

                                    // Fallback: derive tenantId from issuer when tid claim missing (some account types / older tokens)
                                    if (string.IsNullOrWhiteSpace(tenantId))
                                    {
                                        var issuer = issuerClaim;
                                        if (!string.IsNullOrWhiteSpace(issuer))
                                        {
                                            try
                                            {
                                                // Issuer format: https://login.microsoftonline.com/{tenantId}/v2.0
                                                var trimmed = issuer.TrimEnd('/');
                                                var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
                                                tenantId = segments.LastOrDefault(s => s.Length == 36 && s.Count(c => c == '-') == 4) // looks like GUID
                                                           ?? segments.Reverse().FirstOrDefault(s => s.Length == 36 && s.Count(c => c == '-') == 4);
                                                if (!string.IsNullOrWhiteSpace(tenantId))
                                                {
                                                    logger.LogDebug("OnTokenValidated: Derived tenantId {TenantId} from issuer", Chat.Web.Utilities.LogSanitizer.Sanitize(tenantId));
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                logger.LogDebug(ex, "OnTokenValidated: Failed to derive tenantId from issuer {Issuer}", Chat.Web.Utilities.LogSanitizer.Sanitize(issuer));
                                            }
                                        }
                                    }
                                    
                                    // One-time diagnostic: log sanitized UPN, TenantId, Issuer on token validation
                                    logger.LogInformation(
                                        "OnTokenValidated (diag): UPN={Upn}, Tenant={TenantId}, Issuer={Issuer}",
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(upn ?? "<null>"),
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(tenantId ?? "<null>"),
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(issuerClaim ?? "<null>"));
                                    
                                    if (string.IsNullOrWhiteSpace(upn))
                                    {
                                        logger.LogWarning("OnTokenValidated: UPN (preferred_username) claim is missing from token");
                                        context.Fail("UPN claim is required");
                                        return;
                                    }
                                    
                                    // Explicitly deny Microsoft consumer (MSA) accounts
                                    const string MsaTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
                                    var isMsa = (!string.IsNullOrWhiteSpace(tenantId) && string.Equals(tenantId, MsaTenantId, StringComparison.OrdinalIgnoreCase))
                                                || (!string.IsNullOrWhiteSpace(issuerClaim) && issuerClaim.IndexOf("/consumers", StringComparison.OrdinalIgnoreCase) >= 0);
                                    if (isMsa)
                                    {
                                        logger.LogWarning(
                                            "OnTokenValidated: Denying Microsoft consumer (MSA) account. UPN={Upn}, Tenant={TenantId}, Issuer={Issuer}",
                                            Chat.Web.Utilities.LogSanitizer.Sanitize(upn),
                                            Chat.Web.Utilities.LogSanitizer.Sanitize(tenantId ?? "<null>"),
                                            Chat.Web.Utilities.LogSanitizer.Sanitize(issuerClaim ?? "<null>"));
                                        context.HandleResponse();
                                        context.Response.Redirect("/login?reason=not_authorized");
                                        return;
                                    }
                                    
                                    // Validate tenant if configured
                                    if (entraIdOptions.Authorization?.RequireTenantValidation == true &&
                                        entraIdOptions.Authorization?.AllowedTenants?.Count > 0)
                                    {
                                        if (string.IsNullOrWhiteSpace(tenantId) ||
                                            !entraIdOptions.Authorization.AllowedTenants.Contains(tenantId, StringComparer.OrdinalIgnoreCase))
                                        {
                                            logger.LogWarning(
                                                "OnTokenValidated: Tenant {TenantId} is not in AllowedTenants list for UPN {Upn}",
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(tenantId ?? "<null>"),
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(upn));
                                            
                                            // Tenant not authorized - redirect to login
                                            // Both silent SSO and normal login follow same path
                                            context.HandleResponse();
                                            context.Response.Redirect("/login?reason=not_authorized");
                                            return;
                                        }
                                    }
                                    
                                    // Home tenant validation for admin role
                                    // Remove admin role claim from external tenant users for security
                                    var adminRoleValue = entraIdOptions.Authorization?.AdminRoleValue ?? "Admin.ReadWrite";
                                    var hasAdminRole = context.Principal?.IsInRole(adminRoleValue) == true;
                                    
                                    if (hasAdminRole)
                                    {
                                        // Determine home tenant ID (explicit or fallback to TenantId if not "organizations")
                                        var homeTenantId = entraIdOptions.Authorization?.HomeTenantId;
                                        if (string.IsNullOrWhiteSpace(homeTenantId) && 
                                            !string.Equals(entraIdOptions.TenantId, "organizations", StringComparison.OrdinalIgnoreCase))
                                        {
                                            homeTenantId = entraIdOptions.TenantId;
                                        }
                                        
                                        // Check if user is from home tenant
                                        var isHomeTenant = !string.IsNullOrWhiteSpace(homeTenantId) &&
                                                          !string.IsNullOrWhiteSpace(tenantId) &&
                                                          string.Equals(tenantId, homeTenantId, StringComparison.OrdinalIgnoreCase);
                                        
                                        if (!isHomeTenant)
                                        {
                                            // External tenant user with admin role - remove role claim
                                            logger.LogWarning(
                                                "OnTokenValidated: External tenant user {Upn} from tenant {TenantId} has admin role {AdminRole} - removing claim (home tenant: {HomeTenantId})",
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(upn),
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(tenantId ?? "<null>"),
                                                adminRoleValue,
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(homeTenantId ?? "<null>"));
                                            
                                            var identity = context.Principal?.Identity as ClaimsIdentity;
                                            if (identity != null)
                                            {
                                                var roleClaims = identity.FindAll(ClaimTypes.Role)
                                                    .Where(c => string.Equals(c.Value, adminRoleValue, StringComparison.OrdinalIgnoreCase))
                                                    .ToList();
                                                
                                                foreach (var claim in roleClaims)
                                                {
                                                    identity.RemoveClaim(claim);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            logger.LogInformation(
                                                "OnTokenValidated: Home tenant admin user {Upn} authenticated with {AdminRole} role",
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(upn),
                                                adminRoleValue);
                                        }
                                    }
                                    
                                    // Lookup user by UPN (STRICT MATCHING - no fallback to email/username)
                                    // Admin MUST pre-populate the Upn field in database before user's first Entra ID login
                                    // Example: UPDATE users SET upn = 'alice@contoso.com' WHERE username = 'alice'
                                    var user = await usersRepo.GetByUpnAsync(upn);
                                    if (user == null)
                                    {
                                        // User not found by UPN - DENY ACCESS (no auto-provisioning)
                                        // Check if OTP fallback allows unauthorized users
                                        if (entraIdOptions.Fallback?.OtpForUnauthorizedUsers == true)
                                        {
                                            logger.LogInformation(
                                                "OnTokenValidated: User with UPN {Upn} not found in database. OTP fallback enabled - rejecting Entra ID login.",
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(upn));
                                            
                                            // Redirect to login with reason
                                            context.HandleResponse();
                                            context.Response.Redirect("/login?reason=not_authorized");
                                            return;
                                        }
                                        else
                                        {
                                            logger.LogWarning(
                                                "OnTokenValidated: User with UPN {Upn} not found in database and OTP fallback disabled - rejecting login",
                                                Chat.Web.Utilities.LogSanitizer.Sanitize(upn));

                                            // Redirect to login with reason
                                            context.HandleResponse();
                                            context.Response.Redirect("/login?reason=not_authorized");
                                            return;
                                        }
                                    }
                                    
                                    // Update user with Entra ID claims (keep existing data)
                                    user.Upn = upn;
                                    user.TenantId = tenantId;
                                    user.DisplayName = displayName;
                                    // Update FullName from DisplayName on first Entra ID login
                                    if (!string.IsNullOrWhiteSpace(displayName)) user.FullName = displayName;
                                    if (!string.IsNullOrWhiteSpace(email)) user.Email = email;
                                    if (!string.IsNullOrWhiteSpace(country)) user.Country = country;
                                    if (!string.IsNullOrWhiteSpace(region)) user.Region = region;
                                    
                                    await usersRepo.UpsertAsync(user);
                                    
                                    // CRITICAL FIX: Set ClaimTypes.Name to userName (not UPN)
                                    // The app uses Context.User.Identity.Name throughout (ChatHub, controllers, etc.)
                                    // and expects it to match the database userName field, not the UPN
                                    var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;
                                    if (claimsIdentity != null)
                                    {
                                        // Remove existing name claim (which contains UPN)
                                        var existingNameClaim = claimsIdentity.FindFirst(ClaimTypes.Name);
                                        if (existingNameClaim != null)
                                        {
                                            claimsIdentity.RemoveClaim(existingNameClaim);
                                        }
                                        
                                        // Add new name claim with userName from database
                                        claimsIdentity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
                                        
                                        // CRITICAL: Reassign the principal to ensure modified claims are persisted to the authentication cookie
                                        // Without this, SignalR will still see the original UPN in Context.User.Identity.Name
                                        context.Principal = new ClaimsPrincipal(claimsIdentity);
                                        
                                        logger.LogInformation(
                                            "OnTokenValidated: Set ClaimTypes.Name to {UserName} (was UPN: {Upn})",
                                            Chat.Web.Utilities.LogSanitizer.Sanitize(user.UserName),
                                            Chat.Web.Utilities.LogSanitizer.Sanitize(upn));
                                    }
                                    
                                    logger.LogInformation(
                                        "OnTokenValidated: User {UserName} authenticated via Entra ID (UPN: {Upn}, Tenant: {TenantId})",
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(user.UserName),
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(upn),
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(tenantId ?? "<null>"));
                                },
                                OnRemoteFailure = context =>
                                {
                                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
                                    // Check both Parameters and Items for silent flag
                                    var isSilentParams = context.Properties?.Parameters.TryGetValue("silent", out var silentVal) == true &&
                                                         ((silentVal as string) == "true" || (silentVal is bool b && b));
                                    var isSilentItems = context.Properties?.Items.TryGetValue("silent", out var silentItemVal) == true &&
                                                        silentItemVal == "true";
                                    var isSilent = isSilentParams || isSilentItems;
                                    
                                    var errMsg = context.Failure?.Message;
                                    logger.LogInformation("OnRemoteFailure: isSilent={IsSilent} (params={IsSilentParams}, items={IsSilentItems}), message={Message}", 
                                        isSilent, isSilentParams, isSilentItems, Chat.Web.Utilities.LogSanitizer.Sanitize(errMsg ?? "<none>"));
                                    
                                    if (isSilent)
                                    {
                                        context.HandleResponse();
                                        context.Response.Redirect("/login?reason=sso_failed");
                                    }
                                    return Task.CompletedTask;
                                },
                                OnAuthenticationFailed = context =>
                                {
                                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
                                    logger.LogError(
                                        context.Exception,
                                        "OnAuthenticationFailed: Entra ID authentication failed: {ErrorMessage}",
                                        Chat.Web.Utilities.LogSanitizer.Sanitize(context.Exception?.Message ?? "Unknown error"));
                                    context.HandleResponse();
                                    context.Response.Redirect("/login?error=authentication_failed");
                                    return Task.CompletedTask;
                                }
                            };
                        },
                        openIdConnectScheme: "EntraId",
                        subscribeToOpenIdConnectMiddlewareDiagnosticsEvents: false);

                    // Configure the cookie authentication options that AddMicrosoftIdentityWebApp created
                    services.ConfigureApplicationCookie(options =>
                    {
                        options.LoginPath = "/login";
                        options.AccessDeniedPath = "/login";
                        options.SlidingExpiration = true;
                        options.ExpireTimeSpan = TimeSpan.FromHours(12);
                        options.ReturnUrlParameter = "ReturnUrl";
                        
                        // Cookie security settings
                        options.Cookie.HttpOnly = true;
                        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Require HTTPS
                        options.Cookie.SameSite = SameSiteMode.Lax; // Allow cross-site on top-level navigation
                        
                        // Debug: Log what claims are in the cookie
                        options.Events.OnValidatePrincipal = async cookieContext =>
                        {
                            var logger = cookieContext.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
                            var identity = cookieContext.Principal?.Identity as ClaimsIdentity;
                            var name = cookieContext.Principal?.Identity?.Name;
                            var claimCount = identity?.Claims?.Count() ?? 0;
                            var hasNameClaim = identity?.HasClaim(c => c.Type == ClaimTypes.Name) ?? false;
                            var appUsername = identity?.Claims?.FirstOrDefault(c => c.Type == "app_username")?.Value;
                            
                            logger.LogWarning("Cookie OnValidatePrincipal: Name={Name}, HasNameClaim={HasNameClaim}, app_username={AppUsername}, ClaimCount={Count}",
                                name ?? "<null>", hasNameClaim, appUsername ?? "<null>", claimCount);
                            
                            await Task.CompletedTask;
                        };
                    });

                    // Ensure the default challenge scheme uses our Entra ID OIDC scheme
                    services.PostConfigure<AuthenticationOptions>(opts =>
                    {
                        opts.DefaultChallengeScheme = "EntraId";
                    });
                }
                else
                {
                    // No Entra ID - configure standalone cookie authentication for OTP
                    authBuilder.AddCookie(options =>
                    {
                        options.LoginPath = "/login";
                        options.AccessDeniedPath = "/login";
                        options.SlidingExpiration = true;
                        options.ExpireTimeSpan = TimeSpan.FromHours(12);
                        options.ReturnUrlParameter = "ReturnUrl";
                        
                        // Cookie security settings
                        options.Cookie.HttpOnly = true;
                        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Require HTTPS
                        options.Cookie.SameSite = SameSiteMode.Lax; // Allow cross-site on top-level navigation
                    });
                }
            }

            // ==========================================
            // CORS Configuration
            // ==========================================
            var corsOptions = new CorsOptions();
            Configuration.GetSection("Cors").Bind(corsOptions);

            // Validation: Production MUST NOT allow all origins
            if (!HostEnvironment.IsDevelopment() && corsOptions.AllowAllOrigins)
            {
                throw new InvalidOperationException(
                    "Cors:AllowAllOrigins is set to true in non-Development environment. " +
                    "This is a security risk. Set to false and configure Cors:AllowedOrigins instead.");
            }

            services.AddCors(options =>
            {
                options.AddPolicy("SignalRPolicy", builder =>
                {
                    if (corsOptions.AllowAllOrigins)
                    {
                        // Development only - allow all origins
                        _logger.LogWarning(
                            "CORS configured to allow ALL origins (Development mode). " +
                            "This should NEVER be enabled in Production");

                        builder
                            .SetIsOriginAllowed(_ => true)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    }
                    else
                    {
                        // Production/Staging - strict origin whitelist
                        _logger.LogInformation(
                            "CORS configured with allowed origins: {AllowedOrigins}",
                            string.Join(", ", corsOptions.AllowedOrigins));

                        builder
                            .WithOrigins(corsOptions.AllowedOrigins)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    }
                });
            });

            // ==========================================
            // Hub Filters (auto-discovered by SignalR from DI)
            // ==========================================
            services.AddSingleton<IHubFilter, OriginValidationFilter>();

            // ==========================================
            // CORS Configuration
            // ==========================================
            // SignalR transport: use Azure in normal mode, in-memory during tests
            if (string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSignalR(o =>
                {
                    // Prevent idle disconnects when the tab is backgrounded for long periods
                    o.ClientTimeoutInterval = TimeSpan.FromHours(12);
                    // Server pings clients periodically; keep it relatively frequent
                    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
                });
            }
            else
            {
                // Azure App Service injects connection strings as CUSTOMCONNSTR_{name}
                // Explicitly configure Azure SignalR to read from Connection Strings section
                var signalRConn = Configuration.GetConnectionString("SignalR") 
                    ?? Configuration["Azure:SignalR:ConnectionString"];
                
                services.AddSignalR(o =>
                {
                    o.ClientTimeoutInterval = TimeSpan.FromHours(12);
                    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
                })
                .AddAzureSignalR(options =>
                {
                    if (!string.IsNullOrWhiteSpace(signalRConn))
                    {
                        options.ConnectionString = signalRConn;
                    }
                });
            }
            
            // Now that external dependencies (Cosmos client, Redis multiplexer) are registered, configure OpenTelemetry
            services.AddOpenTelemetry()
                .ConfigureResource(rb => rb.AddService(Tracing.ServiceName, serviceVersion: assemblyVersion))
                .WithTracing(builder =>
                {
                    builder.AddSource(Tracing.ServiceName);
                    try
                    {
                        builder.AddAspNetCoreInstrumentation();
                        builder.AddHttpClientInstrumentation();
                        // Redis instrumentation (dependency spans for StackExchange.Redis) is only enabled
                        // when running against real Redis (not in Testing:InMemory mode) because the
                        // instrumentation extension resolves IConnectionMultiplexer from DI at provider
                        // build time. In integration tests we deliberately do not register Redis.
                        if (!string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase))
                        {
                            builder.AddRedisInstrumentation();
                        }
                    }
                    catch { }
                    
                    // Probabilistic sampling: 20% of traces (reduces telemetry volume by 80%)
                    // Errors are always captured regardless of sampling
                    builder.SetSampler(new TraceIdRatioBasedSampler(0.2));
                    
                    AddSelectedExporter(builder, otlpEndpoint, Configuration);
                })
                .WithMetrics(builder =>
                {
                    builder.AddRuntimeInstrumentation();
                    builder.AddAspNetCoreInstrumentation()
                           .AddHttpClientInstrumentation()
                           .AddMeter(Metrics.InstrumentationName);
                    AddSelectedExporter(builder, otlpEndpoint, Configuration);
                });
            services.AddLogging(logging =>
            {
                logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Information);
                logging.AddFilter("Microsoft.Azure.SignalR", LogLevel.Information);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    /// <summary>
    /// Configures the HTTP request pipeline: diagnostics, static assets, routing, security, tracing and SignalR hub.
    /// </summary>
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Data seeding now happens in the background via DataSeederService (IHostedService)
            // This allows the app to start faster and respond to health checks sooner

            // Global exception handler with comprehensive logging (all environments)
            app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
            
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            // Security headers middleware (CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy)
            // Must be early in pipeline to set headers before any response is written
            app.UseMiddleware<SecurityHeadersMiddleware>();

            // Localization middleware (must be before UseRouting)
            app.UseRequestLocalization();

            app.UseRouting();

            // CORS middleware - MUST be after UseRouting, before UseAuthentication
            app.UseCors("SignalRPolicy");

            app.UseRateLimiter();

            // Silent SSO attempt before authentication (one-time prompt=none challenge)
            app.UseMiddleware<SilentSsoMiddleware>();

            // Custom request tracing middleware (adds per-request Activity & trace headers)
            app.UseMiddleware<RequestTracingMiddleware>();

            // Serilog request logging (structured HTTP access logs)
            app.UseSerilogRequestLogging(opts =>
            {
                opts.EnrichDiagnosticContext = (ctx, http) =>
                {
                    if (http.Response.Headers.TryGetValue("X-Trace-Id", out var traceId))
                    {
                        ctx.Set("TraceId", traceId.ToString());
                    }
                };
            });

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<ChatHub>("/chatHub")
                    .RequireCors("SignalRPolicy");  // Apply CORS policy to SignalR hub
                endpoints.MapHealthChecks("/healthz");
                endpoints.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains("ready")
                });
            });
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _loggerFactory?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
