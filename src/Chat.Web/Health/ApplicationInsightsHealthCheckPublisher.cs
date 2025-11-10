using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Health
{
    /// <summary>
    /// Publishes health check results to Application Insights for monitoring and alerting.
    /// Logs all health check results (healthy and unhealthy) for trend analysis.
    /// </summary>
    public class ApplicationInsightsHealthCheckPublisher : IHealthCheckPublisher
    {
        private readonly ILogger<ApplicationInsightsHealthCheckPublisher> _logger;

        public ApplicationInsightsHealthCheckPublisher(ILogger<ApplicationInsightsHealthCheckPublisher> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            var status = report.Status.ToString();
            var duration = report.TotalDuration.TotalMilliseconds;

            if (report.Status == HealthStatus.Healthy)
            {
                _logger.LogInformation(
                    "Health check completed: {Status} in {Duration:F2}ms. All {Count} checks passed.",
                    status, duration, report.Entries.Count);
            }
            else
            {
                var failedChecks = report.Entries
                    .Where(e => e.Value.Status != HealthStatus.Healthy)
                    .Select(e => $"{e.Key}: {e.Value.Status} - {e.Value.Description ?? e.Value.Exception?.Message ?? "No details"}")
                    .ToList();

                _logger.LogWarning(
                    "Health check completed: {Status} in {Duration:F2}ms. {FailedCount}/{TotalCount} checks failed: {FailedChecks}",
                    status, duration, failedChecks.Count, report.Entries.Count, string.Join("; ", failedChecks));
            }

            // Log individual check details for trend analysis
            foreach (var entry in report.Entries)
            {
                var checkDuration = entry.Value.Duration.TotalMilliseconds;
                
                if (entry.Value.Status == HealthStatus.Healthy)
                {
                    _logger.LogDebug(
                        "Health check '{Name}': {Status} in {Duration:F2}ms",
                        entry.Key, entry.Value.Status, checkDuration);
                }
                else
                {
                    _logger.LogError(
                        entry.Value.Exception,
                        "Health check '{Name}': {Status} in {Duration:F2}ms. Description: {Description}",
                        entry.Key, entry.Value.Status, checkDuration, entry.Value.Description ?? "No description");
                }
            }

            return Task.CompletedTask;
        }
    }
}
