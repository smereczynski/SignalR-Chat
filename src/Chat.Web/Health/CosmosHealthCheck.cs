using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Health
{
    public class CosmosHealthCheck : IHealthCheck
    {
        private readonly Chat.Web.Repositories.CosmosClients _clients;
        private readonly ILogger<CosmosHealthCheck> _logger;

        public CosmosHealthCheck(Chat.Web.Repositories.CosmosClients clients, ILogger<CosmosHealthCheck> logger)
        {
            _clients = clients;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Quick ping by reading database properties with 2 second timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                
                var resp = await _clients.Database.ReadAsync(cancellationToken: cts.Token);
                
                if (resp.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.LogDebug("Cosmos DB health check passed");
                    return HealthCheckResult.Healthy();
                }
                else
                {
                    _logger.LogWarning("Cosmos DB health check failed with status {StatusCode}", resp.StatusCode);
                    return HealthCheckResult.Unhealthy($"Cosmos status {resp.StatusCode}");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Cosmos DB health check timeout (2s exceeded)");
                return HealthCheckResult.Unhealthy("Cosmos health check timeout (2s exceeded)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cosmos DB health check failed with exception: {Message}", ex.Message);
                return HealthCheckResult.Unhealthy("Cosmos connection failed", ex);
            }
        }
    }
}
