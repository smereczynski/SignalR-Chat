using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.Azure.Cosmos;
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
        private readonly IUsersRepository _usersRepo;
        private readonly IRoomsRepository _roomsRepo;
        private readonly CosmosClients _cosmosClients;
        private readonly ILogger<DataSeederService> _logger;

        public DataSeederService(
            IUsersRepository usersRepo,
            IRoomsRepository roomsRepo,
            CosmosClients cosmosClients,
            ILogger<DataSeederService> logger)
        {
            _usersRepo = usersRepo;
            _roomsRepo = roomsRepo;
            _cosmosClients = cosmosClients;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Run seeding once at startup
            await SeedIfEmptyAsync();
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
                var existingRooms = (await _roomsRepo.GetAllAsync())?.ToList();
                var hasRooms = existingRooms != null && existingRooms.Any();

                // Check if any users exist
                var existingUsers = (await _usersRepo.GetAllAsync())?.ToList();
                var hasUsers = existingUsers != null && existingUsers.Any();

                if (hasRooms && hasUsers)
                {
                    _logger.LogInformation("Database already contains data (Rooms: {RoomCount}, Users: {UserCount}) - skipping seed",
                        existingRooms.Count,
                        existingUsers.Count);
                    return;
                }

                if (!hasRooms)
                {
                    _logger.LogInformation("Rooms dataset empty - seeding default rooms");
                    await SeedRoomsAsync();
                }

                if (!hasUsers)
                {
                    _logger.LogInformation("Users dataset empty - seeding default users");
                    await SeedUsersAsync();
                }

                _logger.LogInformation("✓ Database seeding completed successfully (RoomsSeeded: {RoomsSeeded}, UsersSeeded: {UsersSeeded})",
                    !hasRooms,
                    !hasUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to seed database - application will continue but may have no initial data");
                // Don't throw - allow app to start even if seeding fails
            }
        }

        private async Task SeedRoomsAsync()
        {
            _logger.LogInformation("Seeding default rooms...");

            var rooms = new[]
            {
                new { id = "general", name = "general", admin = (string)null, users = Array.Empty<string>() },
                new { id = "ops", name = "ops", admin = (string)null, users = Array.Empty<string>() },
                new { id = "random", name = "random", admin = (string)null, users = Array.Empty<string>() }
            };

            foreach (var room in rooms)
            {
                try
                {
                    // Create room document directly in Cosmos using UpsertItemAsync
                    await _cosmosClients.Rooms.UpsertItemAsync(
                        room,
                        new PartitionKey(room.name)
                    );
                    _logger.LogInformation("  ✓ Created room: {RoomName}", room.name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create room: {RoomName}", room.name);
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
                    await _usersRepo.UpsertAsync(user);
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
