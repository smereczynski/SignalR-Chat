using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Chat.Web.Controllers;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Chat.Tests;

public class HealthEndpointTests
{
    [Fact]
    public void PresenceAndChatHealthControllers_AreMarkedWithAuthorize()
    {
        Assert.NotNull(typeof(PresenceController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(typeof(ChatHealthController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public async Task ChatHealthPresence_ReturnsTotalAndGroupedRooms()
    {
        var tracker = new Mock<IPresenceTracker>();
        tracker
            .Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(
            [
                new Chat.Web.ViewModels.UserViewModel { UserName = "alice", CurrentRoom = "pair:dc-a::dc-b" },
                new Chat.Web.ViewModels.UserViewModel { UserName = "bob", CurrentRoom = "pair:dc-a::dc-b" },
                new Chat.Web.ViewModels.UserViewModel { UserName = "charlie", CurrentRoom = "" }
            ]);

        var controller = new ChatHealthController(tracker.Object);

        var result = await controller.Presence();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"total\":3", json);
        Assert.Contains("pair:dc-a::dc-b", json);
        Assert.Contains("\"count\":2", json);
        Assert.DoesNotContain("charlie", json);
    }

    [Fact]
    public void HealthMetricsGet_MapsSnapshotIntoResponsePayload()
    {
        var metrics = new FakeMetrics(new MetricsSnapshot(11, 7, 5, 3, 2, 13, TimeSpan.FromSeconds(42)));
        var controller = new HealthMetricsController(metrics);

        var result = controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"messagesSent\":11", json);
        Assert.Contains("\"roomsJoined\":7", json);
        Assert.Contains("\"otpRequests\":5", json);
        Assert.Contains("\"otpVerifications\":3", json);
        Assert.Contains("\"activeConnections\":2", json);
        Assert.Contains("\"reconnectAttempts\":13", json);
        Assert.Contains("\"uptimeSeconds\":42", json);
        Assert.Contains("timestamp", json);
    }

    private sealed class FakeMetrics(MetricsSnapshot snapshot) : IInProcessMetrics
    {
        public DateTimeOffset StartTime => DateTimeOffset.UtcNow - snapshot.Uptime;

        public void IncMessagesSent() { }
        public void IncRoomsJoined() { }
        public void IncOtpRequests() { }
        public void IncOtpVerifications() { }
        public void IncOtpVerificationRateLimited() { }
        public void IncReconnectAttempt(int attempt, int delayMs) { }
        public void IncActiveConnections() { }
        public void DecActiveConnections() { }
        public void IncRoomPresence(string roomName) { }
        public void DecRoomPresence(string roomName) { }
        public void UserAvailable(string userName) { }
        public void UserUnavailable(string userName) { }
        public void IncMarkReadRateLimitViolation(string userName) { }
        public MetricsSnapshot Snapshot() => snapshot;
    }
}