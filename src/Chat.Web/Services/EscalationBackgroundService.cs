using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Services
{
    public class EscalationBackgroundService : BackgroundService
    {
        private readonly EscalationService _escalations;
        private readonly ILogger<EscalationBackgroundService> _logger;

        public EscalationBackgroundService(EscalationService escalations, ILogger<EscalationBackgroundService> logger)
        {
            _escalations = escalations;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EscalationBackgroundService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _escalations.ProcessDueScheduledAsync(stoppingToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EscalationBackgroundService loop failed");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("EscalationBackgroundService stopped");
        }
    }
}
