using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Web.Controllers;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chat.Tests;

public class DispatchCentersFeatureTests
{
    private static DispatchCentersController CreateController(
        IDispatchCentersRepository dispatchCenters,
        IUsersRepository users)
    {
        var rooms = new InMemoryRoomsRepository();
        var topology = new Chat.Web.Services.DispatchCenterTopologyService(
            dispatchCenters,
            users,
            rooms,
            NullLogger<Chat.Web.Services.DispatchCenterTopologyService>.Instance);
        var controller = new DispatchCentersController(
            dispatchCenters,
            users,
            topology,
            NullLogger<DispatchCentersController>.Instance);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "Admin")
            ],
            "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    [Fact]
    public async Task Create_WithSelfReference_ReturnsBadRequest()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-main" });

        var dto = new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-main",
            Name = "Main DC",
            Country = "PL",
            IfMain = true,
            OfficerUserName = "officer-main",
            CorrespondingDispatchCenterIds = ["dc-main"]
        };

        var result = await controller.Create(dto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("self-reference", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithExistingCorrespondingCenter_ReturnsCreated()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-south" });

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-1",
            Name = "North",
            Country = "PL",
            IfMain = false
        });

        var dto = new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-2",
            Name = "South",
            Country = "DE",
            IfMain = false,
            OfficerUserName = "officer-south",
            CorrespondingDispatchCenterIds = ["dc-1", "dc-1"]
        };

        var result = await controller.Create(dto);

        var created = Assert.IsType<CreatedResult>(result);
        var payload = Assert.IsType<DispatchCenter>(created.Value);
        Assert.Equal("dc-2", payload.Id);
        Assert.Single(payload.CorrespondingDispatchCenterIds);
        Assert.Contains("dc-1", payload.CorrespondingDispatchCenterIds);
    }

    [Fact]
    public async Task Create_WithDuplicateMainPerCountry_ReturnsConflict()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-krakow" });

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-main-pl-1",
            Name = "Warsaw Main",
            Country = "PL",
            IfMain = true
        });

        var dto = new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-main-pl-2",
            Name = "Krakow Main",
            Country = "pl",
            IfMain = true,
            OfficerUserName = "officer-krakow"
        };

        var result = await controller.Create(dto);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("main dispatch center for this country already exists", conflict.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssignUsers_UpdatesDispatchCenterAndUserMembership()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);
        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "officer-ops" });

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-ops",
            Name = "Ops",
            Country = "PL",
            IfMain = true,
            OfficerUserName = "officer-ops"
        });

        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "alice", FullName = "Alice" });

        var result = await controller.AssignUsers("dc-ops", new DispatchCentersController.ManageUsersDto
        {
            UserNames = ["alice", "alice"]
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var dc = Assert.IsType<DispatchCenter>(ok.Value);
        Assert.Single(dc.Users);
        Assert.Contains("alice", dc.Users);

        var alice = await usersRepo.GetByUserNameAsync("alice");
        Assert.NotNull(alice);
        Assert.Equal("dc-ops", alice.DispatchCenterId);
    }

    [Fact]
    public async Task Delete_RemovesReferencesFromUsersAndOtherCenters()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-a",
            Name = "A",
            Country = "PL",
            IfMain = true,
            CorrespondingDispatchCenterIds = ["dc-b"]
        });

        await dispatchRepo.UpsertAsync(new DispatchCenter
        {
            Id = "dc-b",
            Name = "B",
            Country = "DE",
            IfMain = false,
            CorrespondingDispatchCenterIds = ["dc-a"]
        });

        await usersRepo.UpsertAsync(new ApplicationUser
        {
            UserName = "bob",
            FullName = "Bob",
            DispatchCenterId = "dc-b"
        });

        var result = await controller.Delete("dc-b");

        Assert.IsType<NoContentResult>(result);

        var deleted = await dispatchRepo.GetByIdAsync("dc-b");
        Assert.Null(deleted);

        var a = await dispatchRepo.GetByIdAsync("dc-a");
        Assert.NotNull(a);
        Assert.DoesNotContain("dc-b", a.CorrespondingDispatchCenterIds, StringComparer.OrdinalIgnoreCase);

        var bob = await usersRepo.GetByUserNameAsync("bob");
        Assert.NotNull(bob);
        Assert.Null(bob.DispatchCenterId);
    }
}
