using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Chat.Web.Repositories;
using Chat.Web.Models;
using Chat.DataSeed;

namespace Chat.DataSeed.Tests;

public class DataSeederTests
{
    private readonly Mock<IUsersRepository> _mockUsersRepo;
    private readonly Mock<IRoomsRepository> _mockRoomsRepo;
    private readonly Mock<ILogger<DataSeeder>> _mockLogger;
    private readonly DataSeeder _seeder;

    public DataSeederTests()
    {
        _mockUsersRepo = new Mock<IUsersRepository>();
        _mockRoomsRepo = new Mock<IRoomsRepository>();
        _mockLogger = new Mock<ILogger<DataSeeder>>();
        _seeder = new DataSeeder(_mockUsersRepo.Object, _mockRoomsRepo.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SeedAsync_CreatesRooms_WhenTheyDontExist()
    {
        // Arrange
        _mockRoomsRepo.Setup(r => r.GetByName(It.IsAny<string>())).Returns((Room)null);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        // GetByName should be called for each room to check existence
        _mockRoomsRepo.Verify(r => r.GetByName("general"), Times.Once);
        _mockRoomsRepo.Verify(r => r.GetByName("ops"), Times.Once);
        _mockRoomsRepo.Verify(r => r.GetByName("random"), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_SkipsExistingRooms()
    {
        // Arrange
        var existingRoom = new Room { Id = 1, Name = "general", Users = new List<string>() };
        _mockRoomsRepo.Setup(r => r.GetByName("general")).Returns(existingRoom);
        _mockRoomsRepo.Setup(r => r.GetByName("ops")).Returns((Room)null);
        _mockRoomsRepo.Setup(r => r.GetByName("random")).Returns((Room)null);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        // Should log that "general" already exists
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("general") && o.ToString().Contains("already exists")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SeedAsync_CreatesUsers_WhenTheyDontExist()
    {
        // Arrange
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        // Upsert should be called for each user
        _mockUsersRepo.Verify(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "alice")), Times.Once);
        _mockUsersRepo.Verify(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "bob")), Times.Once);
        _mockUsersRepo.Verify(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "charlie")), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_SkipsExistingUsers()
    {
        // Arrange
        var existingUser = new ApplicationUser { UserName = "alice" };
        _mockUsersRepo.Setup(r => r.GetByUserName("alice")).Returns(existingUser);
        _mockUsersRepo.Setup(r => r.GetByUserName("bob")).Returns((ApplicationUser)null);
        _mockUsersRepo.Setup(r => r.GetByUserName("charlie")).Returns((ApplicationUser)null);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        // Alice should not be upserted (already exists)
        _mockUsersRepo.Verify(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "alice")), Times.Never);
        
        // Bob and Charlie should be created
        _mockUsersRepo.Verify(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "bob")), Times.Once);
        _mockUsersRepo.Verify(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "charlie")), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_WithDryRun_DoesNotModifyData()
    {
        // Arrange
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);
        _mockRoomsRepo.Setup(r => r.GetByName(It.IsAny<string>())).Returns((Room)null);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: true);

        // Assert
        // No upserts should happen in dry run mode
        _mockUsersRepo.Verify(r => r.Upsert(It.IsAny<ApplicationUser>()), Times.Never);
        
        // Should still check for existence
        _mockUsersRepo.Verify(r => r.GetByUserName(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SeedAsync_CreatesAliceWithCorrectRooms()
    {
        // Arrange
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);
        ApplicationUser capturedUser = null;
        _mockUsersRepo.Setup(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "alice")))
            .Callback<ApplicationUser>(u => capturedUser = u);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        Assert.NotNull(capturedUser);
        Assert.Equal("alice", capturedUser.UserName);
        Assert.Equal("Alice Johnson", capturedUser.FullName);
        Assert.Equal("alice@example.com", capturedUser.Email);
        Assert.Equal("+1234567890", capturedUser.MobileNumber);
        Assert.True(capturedUser.Enabled);
        Assert.Equal("general", capturedUser.DefaultRoom);
        Assert.Contains("general", capturedUser.FixedRooms);
        Assert.Contains("ops", capturedUser.FixedRooms);
        Assert.Equal(2, capturedUser.FixedRooms.Count);
    }

    [Fact]
    public async Task SeedAsync_CreatesBobWithCorrectRooms()
    {
        // Arrange
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);
        ApplicationUser capturedUser = null;
        _mockUsersRepo.Setup(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "bob")))
            .Callback<ApplicationUser>(u => capturedUser = u);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        Assert.NotNull(capturedUser);
        Assert.Equal("bob", capturedUser.UserName);
        Assert.Equal("Bob Stone", capturedUser.FullName);
        Assert.Contains("general", capturedUser.FixedRooms);
        Assert.Contains("random", capturedUser.FixedRooms);
        Assert.Equal(2, capturedUser.FixedRooms.Count);
    }

    [Fact]
    public async Task SeedAsync_CreatesCharlieWithCorrectRooms()
    {
        // Arrange
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);
        ApplicationUser capturedUser = null;
        _mockUsersRepo.Setup(r => r.Upsert(It.Is<ApplicationUser>(u => u.UserName == "charlie")))
            .Callback<ApplicationUser>(u => capturedUser = u);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert
        Assert.NotNull(capturedUser);
        Assert.Equal("charlie", capturedUser.UserName);
        Assert.Equal("Charlie Fields", capturedUser.FullName);
        Assert.Contains("general", capturedUser.FixedRooms);
        Assert.Single(capturedUser.FixedRooms);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_CanBeRunMultipleTimes()
    {
        // Arrange - first run creates users
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);

        // Act - first run
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Arrange - subsequent runs find existing users
        _mockUsersRepo.Setup(r => r.GetByUserName("alice")).Returns(new ApplicationUser { UserName = "alice" });
        _mockUsersRepo.Setup(r => r.GetByUserName("bob")).Returns(new ApplicationUser { UserName = "bob" });
        _mockUsersRepo.Setup(r => r.GetByUserName("charlie")).Returns(new ApplicationUser { UserName = "charlie" });

        // Reset mock to track second run
        _mockUsersRepo.Invocations.Clear();

        // Act - second run
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert - no upserts on second run
        _mockUsersRepo.Verify(r => r.Upsert(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_LogsProgress()
    {
        // Arrange
        _mockUsersRepo.Setup(r => r.GetByUserName(It.IsAny<string>())).Returns((ApplicationUser)null);
        _mockRoomsRepo.Setup(r => r.GetByName(It.IsAny<string>())).Returns((Room)null);

        // Act
        await _seeder.SeedAsync(clearExisting: false, dryRun: false);

        // Assert - verify logging happened
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("Starting data seed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}
