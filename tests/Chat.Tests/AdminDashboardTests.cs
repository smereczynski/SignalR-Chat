using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Pages.Admin;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Xunit;

namespace Chat.Tests;

public class AdminDashboardTests
{
    private static AdminIndexModel BuildPage(
        InMemoryUsersRepository? users = null,
        InMemoryDispatchCentersRepository? dcs = null,
        InMemoryRoomsRepository? rooms = null,
        InMemoryEscalationsRepository? escalations = null,
        Chat.Web.Services.DispatchCenterTopologyService? topology = null)
    {
        users ??= new InMemoryUsersRepository();
        dcs ??= new InMemoryDispatchCentersRepository();
        rooms ??= new InMemoryRoomsRepository();
        escalations ??= new InMemoryEscalationsRepository();
        topology ??= new Chat.Web.Services.DispatchCenterTopologyService(
            dcs, users, rooms,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Chat.Web.Services.DispatchCenterTopologyService>.Instance);
        var page = new AdminIndexModel(users, dcs, rooms, escalations, topology);
        page.TempData = new Mock<ITempDataDictionary>().Object;
        return page;
    }

    [Fact]
    public async Task OnGet_NoData_StatsAllZero()
    {
        var page = BuildPage();

        await page.OnGetAsync();

        Assert.False(page.Stats.HasWarnings);
        Assert.Equal(0, page.Stats.UsersWithoutDispatchCenter);
        Assert.Equal(0, page.Stats.DispatchCentersWithoutOfficers);
        Assert.Equal(0, page.Stats.DispatchCentersWithoutPairs);
        Assert.Equal(0, page.Stats.InactivePairRooms);
        Assert.Equal(0, page.Stats.OpenEscalations);
    }

    [Fact]
    public async Task OnGet_MixedWarnings_AggregatesAllCounters()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new ApplicationUser { UserName = "alice", DispatchCenterId = null });

        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-1", Name = "DC One", Country = "PL", OfficerUserNames = new List<string>(), CorrespondingDispatchCenterIds = new List<string>() });

        var rooms = new InMemoryRoomsRepository();
        await rooms.UpsertAsync(new Room { Name = "pair:A::B", IsActive = false });

        var escalations = new InMemoryEscalationsRepository();
        await escalations.CreateAsync(new Escalation
        {
            Id = "esc-1",
            RoomName = "pair:A::B",
            Status = EscalationStatus.Escalated,
            CreatedAt = System.DateTime.UtcNow,
            DueAt = System.DateTime.UtcNow
        });

        var page = BuildPage(users: users, dcs: dcs, rooms: rooms, escalations: escalations);
        await page.OnGetAsync();

        Assert.True(page.Stats.HasWarnings);
        Assert.Equal(1, page.Stats.UsersWithoutDispatchCenter);
        Assert.Equal(1, page.Stats.DispatchCentersWithoutOfficers);
        Assert.Equal(1, page.Stats.DispatchCentersWithoutPairs);
        Assert.Equal(1, page.Stats.InactivePairRooms);
        Assert.Equal(1, page.Stats.OpenEscalations);
    }

    [Fact]
    public async Task OnPostSync_CallsTopologySyncAndRedirects()
    {
        var page = BuildPage();

        var result = await page.OnPostSyncAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName); // redirects to same page
    }
}
