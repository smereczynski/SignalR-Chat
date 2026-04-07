using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Pages.Admin.DispatchCenters;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace Chat.Tests;

public class DispatchCentersAdminPageTests
{
    [Fact]
    public async Task CreatePage_OnPost_CreatesDispatchCenterAndRedirects()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var roomsRepo = new InMemoryRoomsRepository();
        var topology = new Chat.Web.Services.DispatchCenterTopologyService(
            dispatchRepo,
            usersRepo,
            roomsRepo,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Chat.Web.Services.DispatchCenterTopologyService>.Instance);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-main" });
        var page = new DispatchCentersCreateModel(dispatchRepo, usersRepo, topology)
        {
            Input = new DispatchCenterInputModel
            {
                Name = "Main DC",
                Country = "PL",
                IfMain = true,
                OfficerUserNames = new List<string> { "officer-main" },
                CorrespondingDispatchCenterIds = new List<string>()
            }
        };

        var result = await page.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);

        var all = (await dispatchRepo.GetAllAsync()).ToList();
        Assert.Single(all);
        Assert.Equal("Main DC", all[0].Name);
        Assert.Equal("PL", all[0].Country);
        Assert.True(all[0].IfMain);
    }

    [Fact]
    public async Task EditPage_OnPost_WithSelfReference_ReturnsPageWithModelError()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var roomsRepo = new InMemoryRoomsRepository();
        var topology = new Chat.Web.Services.DispatchCenterTopologyService(
            dispatchRepo,
            usersRepo,
            roomsRepo,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Chat.Web.Services.DispatchCenterTopologyService>.Instance);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-main" });

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-1",
            Name = "Main",
            Country = "PL",
            IfMain = true,
            OfficerUserNames = new List<string> { "officer-main" }
        });

        var page = new DispatchCentersEditModel(dispatchRepo, usersRepo, topology)
        {
            Id = "dc-1",
            Input = new DispatchCenterInputModel
            {
                Name = "Main",
                Country = "PL",
                IfMain = true,
                OfficerUserNames = new List<string> { "officer-main" },
                CorrespondingDispatchCenterIds = new List<string> { "dc-1" }
            }
        };

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.Contains(page.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("Self-reference"));
    }

    [Fact]
    public async Task CreatePage_OnPost_DuplicateMainPerCountry_ReturnsPageWithModelError()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var roomsRepo = new InMemoryRoomsRepository();
        var topology = new Chat.Web.Services.DispatchCenterTopologyService(
            dispatchRepo,
            usersRepo,
            roomsRepo,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Chat.Web.Services.DispatchCenterTopologyService>.Instance);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-krakow" });

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-main-pl-1",
            Name = "Warsaw Main",
            Country = "PL",
            IfMain = true
        });

        var page = new DispatchCentersCreateModel(dispatchRepo, usersRepo, topology)
        {
            Input = new DispatchCenterInputModel
            {
                Name = "Krakow Main",
                Country = "pl",
                IfMain = true,
                OfficerUserNames = new List<string> { "officer-krakow" },
                CorrespondingDispatchCenterIds = new List<string>()
            }
        };

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.Contains(
            page.ModelState.Values.SelectMany(v => v.Errors),
            e => e.ErrorMessage.Contains("Main dispatch center for this country already exists."));
    }

    [Fact]
    public async Task AssignUsersPage_OnPost_SynchronizesUserMemberships()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var roomsRepo = new InMemoryRoomsRepository();
        var topology = new Chat.Web.Services.DispatchCenterTopologyService(
            dispatchRepo,
            usersRepo,
            roomsRepo,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Chat.Web.Services.DispatchCenterTopologyService>.Instance);

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-ops",
            Name = "Ops",
            Country = "PL",
            IfMain = false,
            Users = new List<string> { "bob" }
        });

        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "alice" });
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "bob", DispatchCenterId = "dc-ops" });

        var page = new DispatchCentersAssignUsersModel(dispatchRepo, usersRepo, topology)
        {
            Id = "dc-ops",
            SelectedUsers = new List<string> { "alice" }
        };

        var result = await page.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Edit", redirect.PageName);

        var dispatchCenter = await dispatchRepo.GetByIdAsync("dc-ops");
        Assert.NotNull(dispatchCenter);
        Assert.Single(dispatchCenter.Users);
        Assert.Contains("alice", dispatchCenter.Users);
        Assert.DoesNotContain("bob", dispatchCenter.Users);

        var alice = await usersRepo.GetByUserNameAsync("alice");
        var bob = await usersRepo.GetByUserNameAsync("bob");

        Assert.Equal("dc-ops", alice.DispatchCenterId);
        Assert.Null(bob.DispatchCenterId);
    }
}
