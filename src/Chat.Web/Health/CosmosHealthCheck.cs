using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chat.Web.Health
{
    public class CosmosHealthCheck : IHealthCheck
    {
        private readonly Chat.Web.Repositories.CosmosClients _clients;
        public CosmosHealthCheck(Chat.Web.Repositories.CosmosClients clients)
        {
            _clients = clients;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Quick ping by reading database properties with 2 second timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                
                var resp = await _clients.Database.ReadAsync(cancellationToken: cts.Token);
                return resp.StatusCode == System.Net.HttpStatusCode.OK
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"Cosmos status {resp.StatusCode}");
            }
            catch (OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy("Cosmos health check timeout (2s exceeded)");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Cosmos connection failed", ex);
            }
        }
    }
}
