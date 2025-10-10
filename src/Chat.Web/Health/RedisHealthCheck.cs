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
                // Use PingAsync with a short timeout behavior via cancellation
                var delayTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var pingTask = db.PingAsync();
                var completed = await Task.WhenAny(pingTask, delayTask).ConfigureAwait(false);
                if (completed == pingTask)
                {
                    var latency = await pingTask.ConfigureAwait(false);
                    return HealthCheckResult.Healthy($"Latency: {latency.TotalMilliseconds} ms");
                }
                return HealthCheckResult.Unhealthy("Redis ping timeout");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis exception", ex);
            }
        }
    }
}
