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

namespace Chat.Web
{
    /// <summary>
    /// Configures dependency injection and the HTTP request pipeline for the chat application.
    /// Responsibilities:
    ///  - Register persistence (Cosmos / InMemory) and OTP (Redis / InMemory)
    ///  - Configure authentication (cookie vs test header auth) & SignalR (Azure vs in-process)
    ///  - Set up OpenTelemetry (Traces + Metrics + Logs) with exporter auto-selection
    ///  - Apply rate limiting to sensitive OTP endpoints
    ///  - Seed baseline data on startup
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
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

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
                });
            });
            // Options
            services.Configure<CosmosOptions>(Configuration.GetSection("Cosmos"));
            services.Configure<RedisOptions>(Configuration.GetSection("Redis"));
            services.Configure<AcsOptions>(Configuration.GetSection("Acs"));
            services.Configure<OtpOptions>(Configuration.GetSection("Otp"));
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
            }
            else
            {
                // Cosmos required (fail fast if placeholder)
                var cosmosConn = ConfigurationGuards.Require(Configuration["Cosmos:ConnectionString"], "Cosmos:ConnectionString");
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
                services.AddSingleton(new CosmosClients(cosmosOpts));
                services.AddSingleton<IUsersRepository, CosmosUsersRepository>();
                services.AddSingleton<IRoomsRepository, CosmosRoomsRepository>();
                services.AddSingleton<IMessagesRepository, CosmosMessagesRepository>();

                // Redis OTP store (fail fast if placeholder)
                var redisConn = ConfigurationGuards.Require(Configuration["Redis:ConnectionString"], "Redis:ConnectionString");
                services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConn));
                services.AddSingleton<IOtpStore, RedisOtpStore>();

                // Health checks for Redis and Cosmos
                services.AddHealthChecks()
                    .AddCheck<Chat.Web.Health.RedisHealthCheck>("redis", tags: new[] { "ready" })
                    .AddCheck<Chat.Web.Health.CosmosHealthCheck>("cosmos", tags: new[] { "ready" });
            }
            // Prefer ACS sender if configured, otherwise console
            var acsConn = Configuration["Acs:ConnectionString"];            
            if (!string.IsNullOrWhiteSpace(acsConn))
            {
                var acsOptions = new AcsOptions
                {
                    ConnectionString = acsConn,
                    EmailFrom = Configuration["Acs:EmailFrom"],
                    SmsFrom = Configuration["Acs:SmsFrom"]
                };
                services.AddSingleton<IOtpSender>(sp => new AcsOtpSender(acsOptions, sp.GetRequiredService<ILogger<AcsOtpSender>>()));
                // Include ACS in health checks if present (config check only)
                services.AddHealthChecks().AddCheck("acs-config", () => HealthCheckResult.Healthy("configured"), tags: new[] { "ready" });
            }
            else
            {
                services.AddSingleton<IOtpSender, ConsoleOtpSender>();
            }
            services.AddRazorPages();
            services.AddControllers();
            services.AddSingleton<Services.IInProcessMetrics, Services.InProcessMetrics>();
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
                        // Preserve ReturnUrl to bounce back to the originally requested page (/chat by default)
                        options.ReturnUrlParameter = "ReturnUrl";
                    });
            }
            // SignalR transport: use Azure in normal mode, in-memory during tests
            if (string.Equals(Configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSignalR();
            }
            else
            {
                services.AddSignalR().AddAzureSignalR();
            }
            
            // Seeding service (runs once at startup)
            // Background service: seeds default room/users if they do not yet exist (idempotent on restart).
            services.AddHostedService<DataSeedHostedService>();
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
