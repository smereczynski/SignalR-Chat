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
    public class Startup
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
                builder.AddAzureMonitorTraceExporter(o => o.ConnectionString = aiConn);
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
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var aiConn = config["ApplicationInsights:ConnectionString"] ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            if (!string.IsNullOrWhiteSpace(aiConn) && string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddAzureMonitorMetricExporter(o => o.ConnectionString = aiConn);
            }
            else if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
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
    /// Chooses a log exporter based on configuration priority (Azure Monitor > OTLP > Console).
    /// </summary>
    private static void AddSelectedExporter(OpenTelemetryLoggerOptions logging, string otlpEndpoint, IConfiguration config)
        {
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var aiConn = config["ApplicationInsights:ConnectionString"] ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            if (!string.IsNullOrWhiteSpace(aiConn) && string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
            {
                logging.AddAzureMonitorLogExporter(o => o.ConnectionString = aiConn);
            }
            else if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                var endpointUri = new Uri(otlpEndpoint);
                var protocol = otlpEndpoint.Contains(":4318") ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
                logging.AddOtlpExporter(o => { o.Endpoint = endpointUri; o.Protocol = protocol; });
            }
            else
            {
                logging.AddConsoleExporter();
            }
        }
        /// <summary>
        /// Constructs the startup instance (configuration injected by host).
        /// </summary>
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
            var inMemoryTest = string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase);
            // Defer OpenTelemetry registration until after external clients (Cosmos, Redis) are registered
            var otlpEndpoint = Configuration["OTel:OtlpEndpoint"]; // e.g. http://localhost:4317 or https://otlp.yourdomain:4317
            var assemblyVersion = typeof(Startup).Assembly.GetName().Version?.ToString() ?? "unknown";

            // OpenTelemetry logging provider (simple; Serilog remains primary)
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
            services.Configure<Chat.Web.Options.NotificationOptions>(Configuration.GetSection("Notifications"));
            services.Configure<Chat.Web.Options.RateLimitingOptions>(Configuration.GetSection("RateLimiting:MarkRead"));
            services.PostConfigure<OtpOptions>(opts =>
            {
                // Allow env var override of pepper per guide: Otp__Pepper
                var envPepper = Environment.GetEnvironmentVariable("Otp__Pepper");
                if (!string.IsNullOrWhiteSpace(envPepper)) opts.Pepper = envPepper;
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
                // Initialize Cosmos DB clients with logging
                services.AddSingleton(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<CosmosClients>>();
                    try
                    {
                        logger.LogInformation("Initializing Cosmos DB clients for database '{Database}' with containers: Users={Users}, Rooms={Rooms}, Messages={Messages}",
                            cosmosOpts.Database, cosmosOpts.UsersContainer, cosmosOpts.RoomsContainer, cosmosOpts.MessagesContainer);
                        var clients = new CosmosClients(cosmosOpts);
                        logger.LogInformation("Cosmos DB clients initialized successfully");
                        return clients;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to initialize Cosmos DB clients. ConnectionString configured: {HasConnectionString}, Database: {Database}",
                            !string.IsNullOrWhiteSpace(cosmosOpts.ConnectionString), cosmosOpts.Database);
                        throw;
                    }
                });
                services.AddSingleton<IUsersRepository, CosmosUsersRepository>();
                services.AddSingleton<IRoomsRepository, CosmosRoomsRepository>();
                services.AddSingleton<IMessagesRepository, CosmosMessagesRepository>();

                // Data seeder service (seeds initial data during startup if database is empty)
                services.AddSingleton<Services.DataSeederService>();

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
                    new System.Globalization.CultureInfo("pl-PL"), // Poland
                    new System.Globalization.CultureInfo("de-DE"), // Germany
                    new System.Globalization.CultureInfo("cs-CZ"), // Czech Republic
                    new System.Globalization.CultureInfo("sk-SK"), // Slovakia
                    new System.Globalization.CultureInfo("uk-UA"), // Ukraine
                    new System.Globalization.CultureInfo("be-BY"), // Belarus
                    new System.Globalization.CultureInfo("lt-LT"), // Lithuania
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
            // Notification plumbing
            services.AddSingleton<Services.INotificationSender, Services.NotificationSender>();
            services.AddSingleton<Services.UnreadNotificationScheduler>(sp =>
                new Services.UnreadNotificationScheduler(
                    sp.GetRequiredService<IRoomsRepository>(),
                    sp.GetRequiredService<IUsersRepository>(),
                    sp.GetRequiredService<IMessagesRepository>(),
                    sp.GetRequiredService<Services.INotificationSender>(),
                    sp.GetRequiredService<IOtpSender>(),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Chat.Web.Options.NotificationOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Services.UnreadNotificationScheduler>>()
                )
            );
            services.AddHostedService(sp => sp.GetRequiredService<Services.UnreadNotificationScheduler>());
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
            if (string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase))
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            }
            else
            {
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options =>
                    {
                        // Redirect unauthenticated users to dedicated login page
                        options.LoginPath = "/login";
                        options.AccessDeniedPath = "/login";
                        options.SlidingExpiration = true;
                        // Keep session active for at least 12 hours to match chat expectations
                        options.ExpireTimeSpan = TimeSpan.FromHours(12);
                        // Preserve ReturnUrl to bounce back to the originally requested page (/chat by default)
                        options.ReturnUrlParameter = "ReturnUrl";
                    });
            }
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
            // Seed initial data if database is empty (only in non-test mode)
            var inMemoryTest = string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase);
            if (!inMemoryTest)
            {
                var seeder = app.ApplicationServices.GetService<Services.DataSeederService>();
                if (seeder != null)
                {
                    // Run seeding synchronously during startup
                    seeder.SeedIfEmptyAsync().GetAwaiter().GetResult();
                }
            }

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

            app.UseRateLimiter();

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
                endpoints.MapHub<ChatHub>("/chatHub");
                endpoints.MapHealthChecks("/healthz");
                endpoints.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains("ready")
                });
            });
        }
    }
}
