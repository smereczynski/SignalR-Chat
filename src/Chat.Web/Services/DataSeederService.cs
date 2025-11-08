using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chat.Web.Services
{
    /// <summary>
    /// Service responsible for seeding initial rooms and users into the database during application startup.
    /// Only seeds data if the database is empty (no rooms and no users exist).
    /// </summary>
    public class DataSeederService
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

        /// <summary>
        /// Seeds initial data if the database is empty.
        /// Checks for existing rooms and users, and only seeds if both are null/empty.
        /// </summary>
        public async Task SeedIfEmptyAsync()
        {
            try
            {
                _logger.LogInformation("Checking if database needs seeding...");

                // Check if any rooms exist
                var existingRooms = _roomsRepo.GetAll()?.ToList();
                var hasRooms = existingRooms != null && existingRooms.Any();

                // Check if any users exist
                var existingUsers = _usersRepo.GetAll()?.ToList();
                var hasUsers = existingUsers != null && existingUsers.Any();

                if (hasRooms || hasUsers)
                {
                    _logger.LogInformation("Database already contains data (Rooms: {RoomCount}, Users: {UserCount}) - skipping seed", 
                        existingRooms?.Count ?? 0, 
                        existingUsers?.Count ?? 0);
                    return;
                }

                _logger.LogInformation("Database is empty - starting seed process");

                await SeedRoomsAsync();
                await SeedUsersAsync();

                _logger.LogInformation("✓ Database seeding completed successfully");
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
                new { id = "1", name = "general", admin = (string)null, users = Array.Empty<string>() },
                new { id = "2", name = "ops", admin = (string)null, users = Array.Empty<string>() },
                new { id = "3", name = "random", admin = (string)null, users = Array.Empty<string>() }
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
                    _logger.LogInformation("  ✓ Created room: {RoomName} (ID: {RoomId})", room.name, room.id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create room: {RoomName}", room.name);
                }
            }
        }

        private Task SeedUsersAsync()
        {
            _logger.LogInformation("Seeding default users...");

            var users = new[]
            {
                new ApplicationUser
                {
                    UserName = "alice",
                    FullName = "Alice Johnson",
                    Email = "alice@example.com",
                    MobileNumber = "+1234567890",
                    Enabled = true,
                    FixedRooms = new List<string> { "general", "ops" },
                    DefaultRoom = "general",
                    Avatar = null
                },
                new ApplicationUser
                {
                    UserName = "bob",
                    FullName = "Bob Stone",
                    Email = "bob@example.com",
                    MobileNumber = "+1234567891",
                    Enabled = true,
                    FixedRooms = new List<string> { "general", "random" },
                    DefaultRoom = "general",
                    Avatar = null
                },
                new ApplicationUser
                {
                    UserName = "charlie",
                    FullName = "Charlie Fields",
                    Email = "charlie@example.com",
                    MobileNumber = "+1234567892",
                    Enabled = true,
                    FixedRooms = new List<string> { "general" },
                    DefaultRoom = "general",
                    Avatar = null
                }
            };

            foreach (var user in users)
            {
                try
                {
                    _usersRepo.Upsert(user);
                    _logger.LogInformation("  ✓ Created user: {UserName} ({FullName})", user.UserName, user.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create user: {UserName}", user.UserName);
                }
            }

            return Task.CompletedTask;
        }
    }
}
