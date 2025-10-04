using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Chat.Web.Repositories;
using Chat.Web.Models;

namespace Chat.Web.Services
{
    /// <summary>
    /// Idempotent startup task seeding a default room and sample users when store is empty.
    /// </summary>
    public class DataSeedHostedService : IHostedService
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly ILogger<DataSeedHostedService> _logger;

    /// <summary>
    /// Creates the hosted service with repositories used for seeding.
    /// </summary>
    public DataSeedHostedService(IRoomsRepository rooms, IUsersRepository users, ILogger<DataSeedHostedService> logger)
        {
            _rooms = rooms;
            _users = users;
            _logger = logger;
        }

    /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Optional gating flag: Seeding:Enabled=true (default true if not set)
                var enabled = Environment.GetEnvironmentVariable("Seeding__Enabled");
                if (!string.IsNullOrWhiteSpace(enabled) && enabled.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Data seeding skipped (Seeding:Enabled=false)");
                    return Task.CompletedTask;
                }
                // Seed default 'general' room if absent
                var existing = _rooms.GetByName("general");
                if (existing == null)
                {
                    _rooms.Create(new Room { Name = "general" });
                    _logger.LogInformation("Seeded default room 'general'.");
                }
                // Seed sample users if none
                var anyUser = _users.GetAll().FirstOrDefault();
                if (anyUser == null)
                {
                    _users.Upsert(new ApplicationUser { UserName = "alice", FullName = "Alice" });
                    _users.Upsert(new ApplicationUser { UserName = "bob", FullName = "Bob" });
                    _logger.LogInformation("Seeded sample users 'alice', 'bob'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding initial data");
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
