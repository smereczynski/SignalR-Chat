using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Chat.Web.Health
{
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _mux;
        private readonly ILogger<RedisHealthCheck> _logger;

        public RedisHealthCheck(IConnectionMultiplexer mux, ILogger<RedisHealthCheck> logger)
        {
            _mux = mux;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var db = _mux.GetDatabase();
                // Use PingAsync with a short timeout (1 second)
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(1));
                
                var latency = await db.PingAsync();
                _logger.LogDebug("Redis health check passed. Latency: {Latency:F2} ms", latency.TotalMilliseconds);
                return HealthCheckResult.Healthy($"Latency: {latency.TotalMilliseconds:F2} ms");
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Redis health check timeout (1s exceeded)");
                return HealthCheckResult.Unhealthy("Redis ping timeout (1s exceeded)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis health check failed with exception: {Message}", ex.Message);
                return HealthCheckResult.Unhealthy("Redis connection failed", ex);
            }
        }
    }
}
