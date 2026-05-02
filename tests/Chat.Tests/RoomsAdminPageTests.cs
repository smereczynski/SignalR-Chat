using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Pages.Admin.Rooms;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Xunit;

namespace Chat.Tests;

public class RoomsAdminPageTests
{
    private static RoomsIndexModel BuildPage(
        InMemoryRoomsRepository? rooms = null,
        InMemoryDispatchCentersRepository? dcs = null,
        InMemoryUsersRepository? users = null)
    {
        rooms ??= new InMemoryRoomsRepository();
        dcs ??= new InMemoryDispatchCentersRepository();
        users ??= new InMemoryUsersRepository();
        var topology = new DispatchCenterTopologyService(
            dcs, users, rooms,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DispatchCenterTopologyService>.Instance);
        var page = new RoomsIndexModel(rooms, dcs, topology);
        page.TempData = new Mock<ITempDataDictionary>().Object;
        return page;
    }

    [Fact]
    public async Task RoomsPage_OnGet_LoadsAllRoomsIncludingInactive()
    {
        var rooms = new InMemoryRoomsRepository();
        await rooms.UpsertAsync(new Room { Name = "pair:A::B", IsActive = true, DispatchCenterAId = "dc-a", DispatchCenterBId = "dc-b" });
        await rooms.UpsertAsync(new Room { Name = "pair:B::C", IsActive = false, DispatchCenterAId = "dc-b", DispatchCenterBId = "dc-c" });

        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-a", Name = "DC Alpha" });
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-b", Name = "DC Beta" });
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-c", Name = "DC Gamma" });

        var page = BuildPage(rooms, dcs);
        await page.OnGetAsync();

        Assert.Equal(2, page.Rooms.Count);
        Assert.Contains(page.Rooms, r => r.Name == "pair:A::B" && r.IsActive);
        Assert.Contains(page.Rooms, r => r.Name == "pair:B::C" && !r.IsActive);
    }

    [Fact]
    public async Task RoomsPage_OnGet_ResolvesDcNames()
    {
        var rooms = new InMemoryRoomsRepository();
        await rooms.UpsertAsync(new Room { Name = "pair:A::B", DispatchCenterAId = "dc-a", DispatchCenterBId = "dc-b" });

        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-a", Name = "Alpha Center" });
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-b", Name = "Beta Center" });

        var page = BuildPage(rooms, dcs);
        await page.OnGetAsync();

        Assert.True(page.DispatchCenterNames.TryGetValue("dc-a", out var nameA));
        Assert.Equal("Alpha Center", nameA);
        Assert.True(page.DispatchCenterNames.TryGetValue("dc-b", out var nameB));
        Assert.Equal("Beta Center", nameB);
    }

    [Fact]
    public async Task RoomsPage_OnGet_ExposesDcObjects()
    {
        var rooms = new InMemoryRoomsRepository();
        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-a", Name = "Alpha", OfficerUserNames = new List<string>() });

        var page = BuildPage(rooms, dcs);
        await page.OnGetAsync();

        Assert.True(page.DispatchCenters.TryGetValue("dc-a", out var dc));
        Assert.Equal("Alpha", dc.Name);
    }

    [Fact]
    public async Task RoomsPage_OnGet_EmptyRepositories_LoadsEmpty()
    {
        var page = BuildPage();
        await page.OnGetAsync();

        Assert.Empty(page.Rooms);
        Assert.Empty(page.DispatchCenterNames);
        Assert.Empty(page.DispatchCenters);
    }

    [Fact]
    public async Task RoomsPage_OnPostSync_RunsTopologyAndRedirects()
    {
        var page = BuildPage();

        var result = await page.OnPostSyncAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
    }
}
