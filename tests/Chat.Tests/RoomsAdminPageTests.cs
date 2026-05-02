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
    public async Task RoomsPage_OnGet_LoadsRoomsAndResolvesDispatchCenterMetadata()
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
        Assert.True(page.DispatchCenterNames.TryGetValue("dc-a", out var nameA));
        Assert.Equal("DC Alpha", nameA);
        Assert.True(page.DispatchCenterNames.TryGetValue("dc-b", out var nameB));
        Assert.Equal("DC Beta", nameB);
        Assert.True(page.DispatchCenters.TryGetValue("dc-a", out var dc));
        Assert.Equal("DC Alpha", dc.Name);
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
