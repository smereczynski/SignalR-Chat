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

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Information()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}");

            // In Production, write to Application Insights if connection string available
            if (string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(aiConnectionString))
            {
                var telemetryConfig = TelemetryConfiguration.CreateDefault();
                telemetryConfig.ConnectionString = aiConnectionString;
                loggerConfig = loggerConfig.WriteTo.ApplicationInsights(telemetryConfig, TelemetryConverter.Traces);
            }
            // Otherwise, if OTLP endpoint configured, use OpenTelemetry sink
            else if (!string.IsNullOrWhiteSpace(otlpEndpoint))
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
