using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Chat.Web.Repositories;
using Chat.Web.Models;
using System.Collections.Generic;

namespace Chat.Web.Services
{
    /// <summary>
    /// Idempotent startup task seeding a default room set and sample users when store is empty.
    /// </summary>
    public class DataSeedHostedService : IHostedService
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly ILogger<DataSeedHostedService> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Creates the hosted service with repositories used for seeding.
        /// </summary>
        public DataSeedHostedService(
            IRoomsRepository rooms, 
            IUsersRepository users, 
            ILogger<DataSeedHostedService> logger,
            IConfiguration configuration)
        {
            _rooms = rooms;
            _users = users;
            _logger = logger;
            _configuration = configuration;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // In test mode, seed synchronously to ensure data is available immediately
            var isTestMode = _configuration.GetValue<bool>("Testing:InMemory");
            if (isTestMode)
            {
                _logger.LogInformation("Running data seeding synchronously (test mode)");
                return SeedDataAsync(cancellationToken);
            }

            // In production, run seeding in background to avoid blocking startup
            // This allows health checks to respond immediately and the app to start faster
            _ = Task.Run(async () => await SeedDataAsync(cancellationToken), cancellationToken);
            return Task.CompletedTask;
        }

        private async Task SeedDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Optional gating flag: Seeding:Enabled=true (default true if not set)
                var enabled = Environment.GetEnvironmentVariable("Seeding__Enabled");
                if (!string.IsNullOrWhiteSpace(enabled) && enabled.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Data seeding skipped (Seeding:Enabled=false)");
                    return;
                }

                // In production: Add a small delay to avoid all instances hitting Cosmos simultaneously on startup
                // In test mode: Skip delay to ensure data is available immediately
                var isTestMode = _configuration.GetValue<bool>("Testing:InMemory");
                if (!isTestMode)
                {
                    var instanceDelay = new Random().Next(100, 1000); // 100-1000ms random delay
                    await Task.Delay(instanceDelay, cancellationToken);
                    _logger.LogInformation("Starting data seeding (delayed {DelayMs}ms to stagger multi-instance startup)", instanceDelay);
                }
                else
                {
                    _logger.LogInformation("Starting data seeding (no delay in test mode)");
                }

                // Ensure baseline rooms
                string[] roomNames = new[] { "general", "ops", "random" };
                // Guard: verify required rooms exist; log if any missing. No mutation since rooms are immutable now.
                foreach (var rn in roomNames)
                {
                    var existing = _rooms.GetByName(rn);
                    if (existing == null)
                    {
                        _logger.LogWarning("Expected static room {Room} missing. Deployment seed process should provision it.", rn);
                    }
                }

                // Seed users only if none exist
                if (!_users.GetAll().Any())
                {
                    _users.Upsert(new ApplicationUser {
                        UserName = "alice",
                        FullName = "Alice Johnson",
                        Email = "michal.s@free-media.eu",
                        MobileNumber = "+48604970937",
                        FixedRooms = new List<string>{ "general", "ops" },
                        DefaultRoom = "general"
                    });
                    _users.Upsert(new ApplicationUser {
                        UserName = "bob",
                        FullName = "Bob Stone",
                        Email = "michal.s@free-media.eu",
                        MobileNumber = "+48604970937",
                        FixedRooms = new List<string>{ "general", "random" },
                        DefaultRoom = "general"
                    });
                    _users.Upsert(new ApplicationUser {
                        UserName = "charlie",
                        FullName = "Charlie Fields",
                        Email = "michal.s@free-media.eu",
                        MobileNumber = "+48604970937",
                        FixedRooms = new List<string>{ "general" },
                        DefaultRoom = "general"
                    });
                    _logger.LogInformation("Seeded initial users with fixed room assignments.");
                }
                else
                {
                    _logger.LogInformation("Data seeding skipped - users already exist");
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding initial data - application will continue without seeded data");
            }
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
