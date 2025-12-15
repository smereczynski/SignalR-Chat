using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Chat.Web.Observability;
using System;
using Microsoft.ApplicationInsights.Extensibility;

namespace Chat.Web
{
    /// <summary>
    /// Application entry point. Configures Serilog early for bootstrap logging and then builds
    /// and runs the generic host (ASP.NET Core + SignalR + OpenTelemetry setup lives in <see cref="Startup"/>).
    /// Keeping Program minimal makes it easy to adapt to top-level statements in the future if desired.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry method responsible for configuring bootstrap logger, building and running the host.
        /// Ensures fatal exceptions are flushed to the sink before process exit.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            var otlpEndpoint = Environment.GetEnvironmentVariable("OTel__OtlpEndpoint");
            var aiConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            // Determine OTLP protocol from endpoint: default to gRPC unless port 4318 is used
            var useHttpProto = !string.IsNullOrWhiteSpace(otlpEndpoint) && otlpEndpoint.Contains(":4318", StringComparison.Ordinal);

            // Development: verbose logging (Debug level) for easier troubleshooting
            // Production: standard logging (Information level) to reduce noise
            var isDevelopment = string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);
            
            // File logging: opt-in via configuration (disabled by default to avoid unnecessary disk I/O)
            // Set Serilog__WriteToFile=true in environment variables to enable
            var writeToFileEnv = Environment.GetEnvironmentVariable("Serilog__WriteToFile");
            var writeToFile = !string.IsNullOrWhiteSpace(writeToFileEnv) && 
                              (string.Equals(writeToFileEnv, "true", StringComparison.OrdinalIgnoreCase) || writeToFileEnv == "1");
            
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Azure.Cosmos", LogEventLevel.Information) // Always log Cosmos operations
                .MinimumLevel.Override("StackExchange.Redis", LogEventLevel.Information) // Always log Redis operations
                .MinimumLevel.Override("Azure.Core", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Azure.Messaging", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Azure", LogEventLevel.Warning) // Suppress Azure SDK verbose traces (Enqueued, Sent, ResponseReceived)
                .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}");
            
            // Conditionally enable file logging if configured
            if (writeToFile)
            {
                loggerConfig = loggerConfig.WriteTo.File(
                    path: "logs/chat-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    retainedFileCountLimit: 7 // Keep last 7 days
                );
            }

            // Write to Application Insights if connection string available (both Dev and Production)
            if (!string.IsNullOrWhiteSpace(aiConnectionString))
            {
                var telemetryConfig = TelemetryConfiguration.CreateDefault();
                telemetryConfig.ConnectionString = aiConnectionString;
                loggerConfig = loggerConfig.WriteTo.ApplicationInsights(telemetryConfig, TelemetryConverter.Traces);
            }
            // If OTLP endpoint configured, also use OpenTelemetry sink
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                loggerConfig = loggerConfig.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint!;
                    options.Protocol = useHttpProto ? Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf : Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
                    options.ResourceAttributes = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["service.name"] = Tracing.ServiceName
                    };
                });
            }

            Log.Logger = loggerConfig.CreateLogger();

            try
            {
                Log.Information("Starting host");
                CreateHostBuilder(args).Build().Run();
            }
            catch (System.Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static string ExtractInstrumentationKey(string connectionString)
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("InstrumentationKey=".Length);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Creates the generic host builder. Delegates most application configuration to <see cref="Startup"/>.
        /// Serilog integration occurs here so framework logs are captured as early as possible.
        /// </summary>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                // Placeholder for early service configuration extension points if needed later.
                .ConfigureServices(_ => { })
                .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }
}
