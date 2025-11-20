using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Services
{
    /// <summary>
    /// Background service that periodically cleans up stale presence entries.
    /// Detects users with expired heartbeats (no activity for >2 minutes) and removes them from presence tracker.
    /// Provides belt-and-suspenders safety when SignalR disconnect events are lost (network issues, ungraceful shutdown).
    /// </summary>
    public class PresenceCleanupService : BackgroundService
    {
        private readonly IPresenceTracker _presenceTracker;
        private readonly ILogger<PresenceCleanupService> _logger;
        private const int CleanupIntervalMinutes = 3;

        public PresenceCleanupService(IPresenceTracker presenceTracker, ILogger<PresenceCleanupService> logger)
        {
            _presenceTracker = presenceTracker;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PresenceCleanupService started. Cleanup interval: {Interval} minutes", CleanupIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(CleanupIntervalMinutes), stoppingToken);
                    await CleanupStalePresenceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown
                    _logger.LogInformation("PresenceCleanupService stopping gracefully");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PresenceCleanupService cleanup loop");
                    // Continue running despite errors
                }
            }

            _logger.LogInformation("PresenceCleanupService stopped");
        }

        private async Task CleanupStalePresenceAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Starting presence cleanup scan");

                // Get all users currently in presence tracker
                var allPresenceUsers = await _presenceTracker.GetAllUsersAsync();
                
                // Get users with active heartbeats (not stale)
                var activeHeartbeats = await _presenceTracker.GetActiveHeartbeatsAsync();
                var activeHeartbeatsSet = activeHeartbeats.ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Find stale users: in presence but no active heartbeat
                var staleUsers = allPresenceUsers
                    .Where(u => !string.IsNullOrWhiteSpace(u.UserName) && !activeHeartbeatsSet.Contains(u.UserName))
                    .ToList();

                if (staleUsers.Count == 0)
                {
                    _logger.LogDebug("No stale presence entries found. Total presence: {Total}, Active heartbeats: {Active}",
                        allPresenceUsers.Count, activeHeartbeats.Count);
                    return;
                }

                _logger.LogInformation(
                    "Found {StaleCount} stale presence entries out of {TotalCount} total (active heartbeats: {ActiveCount})",
                    staleUsers.Count, allPresenceUsers.Count, activeHeartbeats.Count
                );

                // Remove stale entries
                foreach (var staleUser in staleUsers)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await _presenceTracker.RemoveUserAsync(staleUser.UserName);
                        _logger.LogInformation(
                            "Removed stale presence: {User} (room: {Room})",
                            staleUser.UserName, staleUser.CurrentRoom
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove stale presence for user {User}", staleUser.UserName);
                    }
                }

                _logger.LogInformation("Presence cleanup completed. Removed {Count} stale entries", staleUsers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Presence cleanup failed");
            }
        }
    }
}
