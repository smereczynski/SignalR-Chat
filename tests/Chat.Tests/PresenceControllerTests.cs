using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Chat.Web.Controllers;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Chat.Tests;

public class PresenceControllerTests
{
    [Fact]
    public async Task Get_GroupsUsersByNonEmptyRoom()
    {
        var tracker = new Mock<IPresenceTracker>();
        tracker
            .Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(
            [
                new Chat.Web.ViewModels.UserViewModel { UserName = "alice", FullName = "Alice", CurrentRoom = "pair:dc-a::dc-b" },
                new Chat.Web.ViewModels.UserViewModel { UserName = "bob", FullName = "Bob", CurrentRoom = "pair:dc-a::dc-b" },
                new Chat.Web.ViewModels.UserViewModel { UserName = "charlie", FullName = "Charlie", CurrentRoom = "" }
            ]);

        var controller = BuildController(tracker: tracker.Object);

        var result = await controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("pair:dc-a::dc-b", json);
        Assert.Contains("\"count\":2", json);
        Assert.DoesNotContain("charlie", json);
    }

    [Fact]
    public async Task Ping_WithoutIdentity_ReturnsUnauthorized()
    {
        var tracker = new Mock<IPresenceTracker>();
        var controller = BuildController(tracker: tracker.Object, identityName: null);

        var result = await controller.Ping(new PresenceController.PresencePingDto("pair:dc-a::dc-b"));

        Assert.IsType<UnauthorizedResult>(result);
        tracker.Verify(x => x.SetUserRoomAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        tracker.Verify(x => x.UpdateHeartbeatAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Ping_DisabledUser_ReturnsForbid()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new ApplicationUser { UserName = "alice", Enabled = false, DispatchCenterId = "dc-a" });

        var tracker = new Mock<IPresenceTracker>();
        var controller = BuildController(users, tracker.Object, identityName: "alice");

        var result = await controller.Ping(new PresenceController.PresencePingDto("pair:dc-a::dc-b"));

        Assert.IsType<ForbidResult>(result);
        tracker.Verify(x => x.SetUserRoomAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Ping_UnauthorizedRoom_ClearsResolvedRoomButUpdatesHeartbeat()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new ApplicationUser
        {
            UserName = "alice",
            FullName = "Alice Smith",
            Enabled = true,
            DispatchCenterId = "dc-a"
        });

        var rooms = new InMemoryRoomsRepository();
        await rooms.UpsertAsync(new Room
        {
            Name = "pair:dc-b::dc-c",
            IsActive = true,
            RoomType = RoomType.DispatchCenterPair,
            PairKey = "dc-b::dc-c",
            DispatchCenterAId = "dc-b",
            DispatchCenterBId = "dc-c"
        });

        var tracker = new Mock<IPresenceTracker>();
        tracker.Setup(x => x.SetUserRoomAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        tracker.Setup(x => x.UpdateHeartbeatAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var controller = BuildController(users, tracker.Object, identityName: "alice", rooms: rooms);

        var result = await controller.Ping(new PresenceController.PresencePingDto("pair:dc-b::dc-c"));

        Assert.IsType<AcceptedResult>(result);
        tracker.Verify(x => x.SetUserRoomAsync("alice", "Alice Smith", "", ""), Times.Once);
        tracker.Verify(x => x.UpdateHeartbeatAsync("alice"), Times.Once);
    }

    [Fact]
    public async Task Leave_UsesCanonicalProfileUserName()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new ApplicationUser { UserName = "alice", Enabled = true });

        var tracker = new Mock<IPresenceTracker>();
        tracker.Setup(x => x.RemoveUserAsync("alice")).Returns(Task.CompletedTask);

        var controller = BuildController(users, tracker.Object, identityName: "ALICE");

        var result = await controller.Leave();

        Assert.IsType<AcceptedResult>(result);
        tracker.Verify(x => x.RemoveUserAsync("alice"), Times.Once);
    }

    private static PresenceController BuildController(
        InMemoryUsersRepository? users = null,
        IPresenceTracker? tracker = null,
        string? identityName = "alice",
        InMemoryRoomsRepository? rooms = null)
    {
        users ??= new InMemoryUsersRepository();
        rooms ??= new InMemoryRoomsRepository();
        tracker ??= Mock.Of<IPresenceTracker>();

        var controller = new PresenceController(users, rooms, tracker, NullLogger<PresenceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = BuildPrincipal(identityName)
                }
            }
        };

        return controller;
    }

    private static ClaimsPrincipal BuildPrincipal(string? identityName)
    {
        if (identityName == null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, identityName)], "TestAuth"));
    }
}