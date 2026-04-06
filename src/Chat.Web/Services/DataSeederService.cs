#nullable enable

using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chat.Web.Services
{
    /// <summary>
    /// Background service responsible for seeding initial rooms and users into the database during application startup.
    /// Only seeds data if the database is empty (no rooms and no users exist).
    /// Runs in the background to avoid blocking application startup.
    /// </summary>
    public class DataSeederService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataSeederService> _logger;
        
        private IUsersRepository? _usersRepo;
        private IRoomsRepository? _roomsRepo;
        private IDispatchCentersRepository? _dispatchCentersRepo;
        private DispatchCenterTopologyService? _topology;
        private CosmosClients? _cosmosClients;

        public DataSeederService(
            IServiceProvider serviceProvider,
            ILogger<DataSeederService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Resolve dependencies lazily to avoid CosmosClients initialization timing issue
            _usersRepo = _serviceProvider.GetRequiredService<IUsersRepository>();
            _roomsRepo = _serviceProvider.GetRequiredService<IRoomsRepository>();
            _dispatchCentersRepo = _serviceProvider.GetRequiredService<IDispatchCentersRepository>();
            _topology = _serviceProvider.GetRequiredService<DispatchCenterTopologyService>();
            _cosmosClients = _serviceProvider.GetRequiredService<CosmosClients>();
            
            // Run seeding once at startup
            await SeedIfEmptyAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Seeds initial data for any missing dataset.
        /// If rooms are missing, seeds rooms. If users are missing, seeds users.
        /// This avoids a situation where existing users block room seeding.
        /// </summary>
        public async Task SeedIfEmptyAsync()
        {
            try
            {
                // Check if any rooms exist
                var existingRooms = (await _roomsRepo!.GetAllAsync().ConfigureAwait(false))?.ToList();
                var hasRooms = existingRooms != null && existingRooms.Any();

                // Check if any users exist
                var existingUsers = (await _usersRepo!.GetAllAsync().ConfigureAwait(false))?.ToList();
                var hasUsers = existingUsers != null && existingUsers.Any();

                var existingDispatchCenters = (await _dispatchCentersRepo!.GetAllAsync().ConfigureAwait(false))?.ToList();
                var hasDispatchCenters = existingDispatchCenters != null && existingDispatchCenters.Any();

                if (hasRooms && hasUsers && hasDispatchCenters)
                {
                    _logger.LogInformation("Database already contains data (Rooms: {RoomCount}, Users: {UserCount}, DispatchCenters: {DispatchCenterCount}) - skipping seed",
                        existingRooms!.Count,
                        existingUsers!.Count,
                        existingDispatchCenters!.Count);
                    return;
                }

                if (!hasDispatchCenters)
                {
                    _logger.LogInformation("Dispatch centers dataset empty - seeding default dispatch centers");
                    await SeedDispatchCentersAsync().ConfigureAwait(false);
                }

                if (!hasUsers)
                {
                    _logger.LogInformation("Users dataset empty - seeding default users");
                    await SeedUsersAsync().ConfigureAwait(false);
                }

                await _topology!.SyncRoomsAsync().ConfigureAwait(false);

                _logger.LogInformation("✓ Database seeding completed successfully (DispatchCentersSeeded: {DispatchCentersSeeded}, UsersSeeded: {UsersSeeded})",
                    !hasDispatchCenters,
                    !hasUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to seed database - application will continue but may have no initial data");
                // Don't throw - allow app to start even if seeding fails
            }
        }

        private async Task SeedDispatchCentersAsync()
        {
            _logger.LogInformation("Seeding default dispatch centers...");

            var dispatchCenters = new[]
            {
                new DispatchCenter
                {
                    Id = "dc-pl-main",
                    Name = "Poland Main",
                    Country = "PL",
                    IfMain = true,
                    OfficerUserName = "alice@example.com",
                    CorrespondingDispatchCenterIds = new List<string> { "dc-de-berlin" },
                    Users = new List<string>()
                },
                new DispatchCenter
                {
                    Id = "dc-de-berlin",
                    Name = "Berlin Dispatch",
                    Country = "DE",
                    IfMain = true,
                    OfficerUserName = "bob@example.com",
                    CorrespondingDispatchCenterIds = new List<string> { "dc-pl-main" },
                    Users = new List<string>()
                }
            };

            foreach (var dispatchCenter in dispatchCenters)
            {
                try
                {
                    await _dispatchCentersRepo!.UpsertAsync(dispatchCenter).ConfigureAwait(false);
                    _logger.LogInformation("  ✓ Created dispatch center: {DispatchCenterName}", dispatchCenter.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create dispatch center: {DispatchCenterName}", dispatchCenter.Name);
                }
            }
        }

        private async Task SeedUsersAsync()
        {
            _logger.LogInformation("Seeding default users...");

            var users = new[]
            {
                new ApplicationUser
                {
                    UserName = "alice@example.com",
                    FullName = "Alice Johnson",
                    Email = "alice@example.com",
                    MobileNumber = "+1234567890",
                    Enabled = true,
                    DispatchCenterId = "dc-pl-main",
                    FixedRooms = new List<string> { "general", "ops" },
                    DefaultRoom = "general",
                    Avatar = null,
                    // Entra ID fields (placeholders for seeded users - will be populated on first Entra ID login)
                    Upn = null,
                    TenantId = null,
                    DisplayName = null,
                    Country = null,
                    Region = null
                },
                new ApplicationUser
                {
                    UserName = "bob@example.com",
                    FullName = "Bob Stone",
                    Email = "bob@example.com",
                    MobileNumber = "+1234567891",
                    Enabled = true,
                    DispatchCenterId = "dc-de-berlin",
                    FixedRooms = new List<string> { "general", "random" },
                    DefaultRoom = "general",
                    Avatar = null,
                    // Entra ID fields (placeholders for seeded users - will be populated on first Entra ID login)
                    Upn = null,
                    TenantId = null,
                    DisplayName = null,
                    Country = null,
                    Region = null
                },
                new ApplicationUser
                {
                    UserName = "charlie@example.com",
                    FullName = "Charlie Fields",
                    Email = "charlie@example.com",
                    MobileNumber = "+1234567892",
                    Enabled = true,
                    DispatchCenterId = "dc-pl-main",
                    FixedRooms = new List<string> { "general" },
                    DefaultRoom = "general",
                    Avatar = null,
                    // Entra ID fields (placeholders for seeded users - will be populated on first Entra ID login)
                    Upn = null,
                    TenantId = null,
                    DisplayName = null,
                    Country = null,
                    Region = null
                }
            };

            foreach (var user in users)
            {
                try
                {
                    await _usersRepo!.UpsertAsync(user).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(user.DispatchCenterId))
                    {
                        await _topology!.AssignUserAsync(user.DispatchCenterId, user.UserName).ConfigureAwait(false);
                    }
                    _logger.LogInformation("  ✓ Created user: {UserName} ({FullName})", user.UserName, user.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create user: {UserName}", user.UserName);
                }
            }
        }
    }
}
