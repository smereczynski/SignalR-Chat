using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Hubs;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Chat.Tests;

public class EscalationServiceTests
{
    [Fact]
    public async Task CreateManualAsync_NotifiesAllTargetOfficers()
    {
        var escalations = new InMemoryEscalationsRepository();
        var messages = new InMemoryMessagesRepository();
        var rooms = new InMemoryRoomsRepository();
        var users = new InMemoryUsersRepository();
        var dispatchCenters = new InMemoryDispatchCentersRepository();

        var sender = new ApplicationUser
        {
            UserName = "sender",
            DispatchCenterId = "dc-a",
            Enabled = true
        };
        var officer1 = new ApplicationUser { UserName = "officer-1", Enabled = true };
        var officer2 = new ApplicationUser { UserName = "officer-2", Enabled = true };

        await users.UpsertAsync(sender);
        await users.UpsertAsync(officer1);
        await users.UpsertAsync(officer2);

        await dispatchCenters.UpsertAsync(new DispatchCenter
        {
            Id = "dc-a",
            Name = "Alpha",
            Country = "PL",
            OfficerUserNames = new List<string> { "source-officer" }
        });
        await dispatchCenters.UpsertAsync(new DispatchCenter
        {
            Id = "dc-b",
            Name = "Beta",
            Country = "DE",
            OfficerUserNames = new List<string> { "officer-1", "officer-2" }
        });

        var room = new Room
        {
            Name = "pair:dc-a::dc-b",
            DisplayName = "Alpha <-> Beta",
            RoomType = RoomType.DispatchCenterPair,
            PairKey = "dc-a::dc-b",
            DispatchCenterAId = "dc-a",
            DispatchCenterBId = "dc-b",
            IsActive = true
        };
        await rooms.UpsertAsync(room);

        var message = await messages.CreateAsync(new Message
        {
            Content = "Need escalation",
            FromUser = sender,
            FromDispatchCenterId = "dc-a",
            ToRoom = room,
            Timestamp = System.DateTime.UtcNow
        });

        var groupProxy = new Mock<IClientProxy>();
        groupProxy
            .Setup(x => x.SendCoreAsync("escalationChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(room.Name)).Returns(groupProxy.Object);

        var hubContext = new Mock<IHubContext<ChatHub>>();
        hubContext.SetupGet(x => x.Clients).Returns(clients.Object);

        var notifications = new Mock<INotificationSender>();
        notifications
            .Setup(x => x.NotifyAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<Message>()))
            .Returns(Task.CompletedTask);

        var service = new EscalationService(
            escalations,
            messages,
            rooms,
            users,
            dispatchCenters,
            hubContext.Object,
            notifications.Object,
            NullLogger<EscalationService>.Instance);

        var escalation = await service.CreateManualAsync(sender, room.Name, new[] { message.Id });

        Assert.NotNull(escalation);
        Assert.Equal(2, escalation.TargetOfficerUserNames.Count);
        notifications.Verify(x => x.NotifyAsync(It.Is<ApplicationUser>(u => u.UserName == "officer-1"), room.DisplayName, It.IsAny<Message>()), Times.Once);
        notifications.Verify(x => x.NotifyAsync(It.Is<ApplicationUser>(u => u.UserName == "officer-2"), room.DisplayName, It.IsAny<Message>()), Times.Once);
    }
}
