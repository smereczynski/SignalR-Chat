using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Chat.Web.Observability;

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
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Information()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")
                .CreateLogger();

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
