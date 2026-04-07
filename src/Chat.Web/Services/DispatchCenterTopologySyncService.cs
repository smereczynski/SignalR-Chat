using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Services
{
    /// <summary>
    /// Rebuilds derived pair rooms from dispatch-center topology during application startup.
    /// This keeps existing databases consistent even when no admin mutation has occurred yet.
    /// </summary>
    public class DispatchCenterTopologySyncService : IHostedService
    {
        private readonly DispatchCenterTopologyService _topology;
        private readonly ILogger<DispatchCenterTopologySyncService> _logger;

        public DispatchCenterTopologySyncService(
            DispatchCenterTopologyService topology,
            ILogger<DispatchCenterTopologySyncService> logger)
        {
            _topology = topology;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Reconciling dispatch-center pair rooms from current topology");
                await _topology.SyncRoomsAsync().ConfigureAwait(false);
                _logger.LogInformation("Dispatch-center topology reconciliation completed");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Dispatch-center topology reconciliation failed during startup");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
