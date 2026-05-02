using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Chat.Web.Observability;
using System;
using System.IO;
using System.Threading.Tasks;
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
        private sealed record BootstrapSettings(
            string OtlpEndpoint,
            string ApplicationInsightsConnectionString,
            string EnvironmentName,
            bool UseHttpProtocol,
            bool IsDevelopment,
            bool WriteToConsole,
            bool WriteToFile);

        /// <summary>
        /// Main entry method responsible for configuring bootstrap logger, building and running the host.
        /// Ensures fatal exceptions are flushed to the sink before process exit.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static async Task Main(string[] args)
        {
            var settings = BuildBootstrapSettings();
            var loggerConfig = CreateBootstrapLoggerConfiguration(settings);

            Log.Logger = loggerConfig.CreateLogger();

            try
            {
                Log.Information("Starting host");
                var host = CreateHostBuilder(args).Build();
                await AwaitCosmosInitializationAsync(host.Services).ConfigureAwait(false);
                await host.RunAsync().ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                await Log.CloseAndFlushAsync().ConfigureAwait(false);
            }
        }

        private static BootstrapSettings BuildBootstrapSettings()
        {
            var otlpEndpoint = Environment.GetEnvironmentVariable("OTel__OtlpEndpoint");
            var aiConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production;
            var isDevelopment = string.Equals(envName, Environments.Development, StringComparison.OrdinalIgnoreCase);
            var bootstrapConfig = BuildBootstrapConfiguration(envName);
            var writeToConsole = bootstrapConfig.GetValue<bool?>("Serilog:WriteToConsole") ?? isDevelopment;

            return new BootstrapSettings(
                otlpEndpoint,
                aiConnectionString,
                envName,
                !string.IsNullOrWhiteSpace(otlpEndpoint) && otlpEndpoint.Contains(":4318", StringComparison.Ordinal),
                isDevelopment,
                writeToConsole,
                IsEnabled(Environment.GetEnvironmentVariable("Serilog__WriteToFile")));
        }

        private static IConfiguration BuildBootstrapConfiguration(string envName)
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{envName}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
        }

        private static LoggerConfiguration CreateBootstrapLoggerConfiguration(BootstrapSettings settings)
        {
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", settings.IsDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Azure.Cosmos", LogEventLevel.Information) // Always log Cosmos operations
                .MinimumLevel.Override("StackExchange.Redis", LogEventLevel.Information) // Always log Redis operations
                .MinimumLevel.Override("Azure.Core", settings.IsDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Azure.Messaging", settings.IsDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("Azure", LogEventLevel.Warning) // Suppress Azure SDK verbose traces (Enqueued, Sent, ResponseReceived)
                .MinimumLevel.Is(settings.IsDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId();

            loggerConfig = loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}",
                restrictedToMinimumLevel: settings.WriteToConsole ? LogEventLevel.Verbose : LogEventLevel.Error,
                standardErrorFromLevel: LogEventLevel.Error);

            if (settings.WriteToFile)
            {
                loggerConfig = loggerConfig.WriteTo.File(
                    path: "logs/chat-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    retainedFileCountLimit: 7 // Keep last 7 days
                );
            }

            if (!string.IsNullOrWhiteSpace(settings.ApplicationInsightsConnectionString))
            {
                var telemetryConfig = TelemetryConfiguration.CreateDefault();
                telemetryConfig.ConnectionString = settings.ApplicationInsightsConnectionString;
                loggerConfig = loggerConfig.WriteTo.ApplicationInsights(telemetryConfig, TelemetryConverter.Traces);
            }

            if (!string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
            {
                loggerConfig = loggerConfig.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = settings.OtlpEndpoint!;
                    options.Protocol = settings.UseHttpProtocol
                        ? Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf
                        : Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
                    options.ResourceAttributes = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["service.name"] = Tracing.ServiceName
                    };
                });
            }

            return loggerConfig;
        }

        private static bool IsEnabled(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1");
        }

        private static async Task AwaitCosmosInitializationAsync(IServiceProvider services)
        {
            var cosmosTask = services.GetService<Task<Repositories.CosmosClients>>();
            if (cosmosTask == null)
            {
                Log.Debug("Cosmos DB initialization task not registered (in-memory mode)");
                return;
            }

            Log.Information("Awaiting Cosmos DB initialization before accepting requests");
            await cosmosTask.ConfigureAwait(false);
            Log.Information("Cosmos DB initialization complete");
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
                // Avoid default Console/Debug providers emitting to stdout/stderr. Serilog and OpenTelemetry
                // logging exporters are configured separately (Startup + bootstrap logger).
                .ConfigureLogging(logging => logging.ClearProviders())
                .UseSerilog()
                // Placeholder for early service configuration extension points if needed later.
                .ConfigureServices(_ => { })
                .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }
}
