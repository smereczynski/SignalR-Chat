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
                // Quick ping by reading database properties
                var resp = await _clients.Database.ReadAsync(cancellationToken: cancellationToken);
                return resp.StatusCode == System.Net.HttpStatusCode.OK
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"Cosmos status {resp.StatusCode}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Cosmos exception", ex);
            }
        }
    }
}
