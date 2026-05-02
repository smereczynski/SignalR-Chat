using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Services;
using Chat.Web.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Chat.Tests;

public class PresenceCleanupServiceTests
{
    private readonly Mock<IPresenceTracker> _presenceTracker = new();
    private readonly Mock<ILogger<PresenceCleanupService>> _logger = new();

    [Fact]
    public async Task CleanupStalePresenceAsync_NoStaleUsers_DoesNothing()
    {
        _presenceTracker
            .Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(
            [
                new UserViewModel { UserName = "alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "bob", CurrentRoom = "general" }
            ]);
        _presenceTracker
            .Setup(x => x.GetActiveHeartbeatsAsync())
            .ReturnsAsync(["alice", "bob"]);

        await InvokeCleanupAsync();

        _presenceTracker.Verify(x => x.RemoveUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CleanupStalePresenceAsync_RemovesOnlyUsersWithoutActiveHeartbeat()
    {
        _presenceTracker
            .Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(
            [
                new UserViewModel { UserName = "alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "bob", CurrentRoom = "general" },
                new UserViewModel { UserName = "charlie", CurrentRoom = "ops" }
            ]);
        _presenceTracker
            .Setup(x => x.GetActiveHeartbeatsAsync())
            .ReturnsAsync(["alice"]);
        _presenceTracker
            .Setup(x => x.RemoveUserAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await InvokeCleanupAsync();

        _presenceTracker.Verify(x => x.RemoveUserAsync("bob"), Times.Once);
        _presenceTracker.Verify(x => x.RemoveUserAsync("charlie"), Times.Once);
        _presenceTracker.Verify(x => x.RemoveUserAsync("alice"), Times.Never);
    }

    [Fact]
    public async Task CleanupStalePresenceAsync_UsesCaseInsensitiveHeartbeatMatching_AndSkipsBlankUserNames()
    {
        _presenceTracker
            .Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(
            [
                new UserViewModel { UserName = "Alice", CurrentRoom = "general" },
                new UserViewModel { UserName = "BOB", CurrentRoom = "ops" },
                new UserViewModel { UserName = "", CurrentRoom = "general" },
                new UserViewModel { UserName = null!, CurrentRoom = "general" }
            ]);
        _presenceTracker
            .Setup(x => x.GetActiveHeartbeatsAsync())
            .ReturnsAsync(["alice", "bob"]);

        await InvokeCleanupAsync();

        _presenceTracker.Verify(x => x.RemoveUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CleanupStalePresenceAsync_RemovalFailure_DoesNotStopLaterRemovals()
    {
        _presenceTracker
            .Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(
            [
                new UserViewModel { UserName = "bob", CurrentRoom = "general" },
                new UserViewModel { UserName = "charlie", CurrentRoom = "ops" }
            ]);
        _presenceTracker
            .Setup(x => x.GetActiveHeartbeatsAsync())
            .ReturnsAsync(new List<string>());
        _presenceTracker
            .Setup(x => x.RemoveUserAsync("bob"))
            .ThrowsAsync(new Exception("Redis connection lost"));
        _presenceTracker
            .Setup(x => x.RemoveUserAsync("charlie"))
            .Returns(Task.CompletedTask);

        await InvokeCleanupAsync();

        _presenceTracker.Verify(x => x.RemoveUserAsync("bob"), Times.Once);
        _presenceTracker.Verify(x => x.RemoveUserAsync("charlie"), Times.Once);
    }

    private async Task InvokeCleanupAsync()
    {
        var service = new PresenceCleanupService(_presenceTracker.Object, _logger.Object);
        var method = typeof(PresenceCleanupService).GetMethod("CleanupStalePresenceAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = method!.Invoke(service, [CancellationToken.None]) as Task;
        Assert.NotNull(task);
        await task!;
    }
}
