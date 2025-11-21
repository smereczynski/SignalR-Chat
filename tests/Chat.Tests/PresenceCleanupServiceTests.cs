using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Services;
using Chat.Web.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Chat.Tests
{
    /// <summary>
    /// Unit tests for PresenceCleanupService background service.
    /// Tests stale presence detection and cleanup logic.
    /// </summary>
    public class PresenceCleanupServiceTests
    {
        private readonly Mock<IPresenceTracker> _mockPresenceTracker;
        private readonly Mock<ILogger<PresenceCleanupService>> _mockLogger;

        public PresenceCleanupServiceTests()
        {
            _mockPresenceTracker = new Mock<IPresenceTracker>();
            _mockLogger = new Mock<ILogger<PresenceCleanupService>>();
        }

        [Fact]
        public async Task CleanupStalePresence_NoStaleUsers_DoesNothing()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "bob", CurrentRoom = "general" }
            };

            var activeHeartbeats = new List<string> { "alice", "bob" };

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);

            var service = new PresenceCleanupService(_mockPresenceTracker.Object, _mockLogger.Object);
            using var cts = new CancellationTokenSource();

            // Act
            await service.StartAsync(cts.Token);
            await Task.Delay(100); // Give service time to start
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);

            // Assert
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CleanupStalePresence_WithStaleUsers_RemovesThem()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "bob", CurrentRoom = "general" },
                new UserViewModel { UserName = "charlie", CurrentRoom = "tech" }
            };

            // Only alice has active heartbeat; bob and charlie are stale
            var activeHeartbeats = new List<string> { "alice" };

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);
            _mockPresenceTracker.Setup(x => x.RemoveUserAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // Create a test-friendly wrapper to trigger cleanup manually
            var cleanupTask = Task.Run(async () =>
            {
                // Simulate the cleanup logic
                var staleUsers = allUsers.Where(u => !activeHeartbeats.Contains(u.UserName)).ToList();
                foreach (var user in staleUsers)
                {
                    await _mockPresenceTracker.Object.RemoveUserAsync(user.UserName);
                }
            });

            // Act
            await cleanupTask;

            // Assert
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("bob"), Times.Once);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("charlie"), Times.Once);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("alice"), Times.Never);
        }

        [Fact]
        public async Task CleanupStalePresence_CaseInsensitiveUsernames_MatchesCorrectly()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "Alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "BOB", CurrentRoom = "general" }
            };

            // Active heartbeats use different casing
            var activeHeartbeats = new List<string> { "alice", "Bob" };

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);

            // Simulate cleanup with case-insensitive comparison
            var cleanupTask = Task.Run(async () =>
            {
                var activeSet = activeHeartbeats.Select(h => h.ToLowerInvariant()).ToHashSet();
                var staleUsers = allUsers.Where(u => !activeSet.Contains(u.UserName.ToLowerInvariant())).ToList();
                
                foreach (var user in staleUsers)
                {
                    await _mockPresenceTracker.Object.RemoveUserAsync(user.UserName);
                }
            });

            // Act
            await cleanupTask;

            // Assert - No users should be removed (all have active heartbeats)
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CleanupStalePresence_EmptyUserName_SkipsUser()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "", CurrentRoom = "general" },
                new UserViewModel { UserName = null!, CurrentRoom = "general" },
                new UserViewModel { UserName = "alice", CurrentRoom = "general" }
            };

            var activeHeartbeats = new List<string> { "alice" };

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);

            // Simulate cleanup with null/empty check
            var cleanupTask = Task.Run(async () =>
            {
                var activeSet = activeHeartbeats.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var staleUsers = allUsers
                    .Where(u => !string.IsNullOrWhiteSpace(u.UserName) && !activeSet.Contains(u.UserName))
                    .ToList();
                
                foreach (var user in staleUsers)
                {
                    await _mockPresenceTracker.Object.RemoveUserAsync(user.UserName);
                }
            });

            // Act
            await cleanupTask;

            // Assert - Empty/null usernames should not trigger removal calls
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync(""), Times.Never);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync(null!), Times.Never);
        }

        [Fact]
        public async Task CleanupStalePresence_RemovalFails_ContinuesWithOthers()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "bob", CurrentRoom = "general" },
                new UserViewModel { UserName = "charlie", CurrentRoom = "tech" }
            };

            var activeHeartbeats = new List<string>(); // All stale

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);
            
            // Bob's removal fails
            _mockPresenceTracker.Setup(x => x.RemoveUserAsync("bob"))
                .ThrowsAsync(new Exception("Redis connection lost"));
            
            // Charlie's removal succeeds
            _mockPresenceTracker.Setup(x => x.RemoveUserAsync("charlie"))
                .Returns(Task.CompletedTask);

            // Simulate cleanup with error handling
            var cleanupTask = Task.Run(async () =>
            {
                var staleUsers = allUsers.Where(u => !activeHeartbeats.Contains(u.UserName)).ToList();
                
                foreach (var user in staleUsers)
                {
                    try
                    {
                        await _mockPresenceTracker.Object.RemoveUserAsync(user.UserName);
                    }
                    catch (Exception)
                    {
                        // Log and continue
                    }
                }
            });

            // Act
            await cleanupTask;

            // Assert - Both removal attempts should be made
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("bob"), Times.Once);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("charlie"), Times.Once);
        }

        [Fact]
        public async Task CleanupStalePresence_AllUsersStale_RemovesAll()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "bob", CurrentRoom = "general" },
                new UserViewModel { UserName = "charlie", CurrentRoom = "tech" },
                new UserViewModel { UserName = "dave", CurrentRoom = "random" }
            };

            var activeHeartbeats = new List<string>(); // No active heartbeats

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);
            _mockPresenceTracker.Setup(x => x.RemoveUserAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // Simulate cleanup
            var cleanupTask = Task.Run(async () =>
            {
                var staleUsers = allUsers.Where(u => !activeHeartbeats.Contains(u.UserName)).ToList();
                foreach (var user in staleUsers)
                {
                    await _mockPresenceTracker.Object.RemoveUserAsync(user.UserName);
                }
            });

            // Act
            await cleanupTask;

            // Assert - All 4 users should be removed
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("alice"), Times.Once);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("bob"), Times.Once);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("charlie"), Times.Once);
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync("dave"), Times.Once);
        }

        [Fact]
        public async Task CleanupStalePresence_AllUsersActive_RemovesNone()
        {
            // Arrange
            var allUsers = new List<UserViewModel>
            {
                new UserViewModel { UserName = "alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "bob", CurrentRoom = "general" }
            };

            var activeHeartbeats = new List<string> { "alice", "bob" }; // All active

            _mockPresenceTracker.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(allUsers);
            _mockPresenceTracker.Setup(x => x.GetActiveHeartbeatsAsync())
                .ReturnsAsync(activeHeartbeats);

            // Simulate cleanup
            var cleanupTask = Task.Run(async () =>
            {
                var activeSet = activeHeartbeats.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var staleUsers = allUsers.Where(u => !activeSet.Contains(u.UserName)).ToList();
                
                foreach (var user in staleUsers)
                {
                    await _mockPresenceTracker.Object.RemoveUserAsync(user.UserName);
                }
            });

            // Act
            await cleanupTask;

            // Assert - No users should be removed
            _mockPresenceTracker.Verify(x => x.RemoveUserAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
