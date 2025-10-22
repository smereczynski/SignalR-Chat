using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Chat.Web.Health
{
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _mux;
        public RedisHealthCheck(IConnectionMultiplexer mux)
        {
            _mux = mux;
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
                return HealthCheckResult.Healthy($"Latency: {latency.TotalMilliseconds:F2} ms");
            }
            catch (OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy("Redis ping timeout (1s exceeded)");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis connection failed", ex);
            }
        }
    }
}
