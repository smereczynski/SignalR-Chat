using System.Collections.Generic;
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

public class DispatchCentersControllerFlowTests
{
    private static DispatchCentersController CreateController(
        IDispatchCentersRepository dispatchCenters,
        IUsersRepository users)
    {
        var controller = new DispatchCentersController(
            dispatchCenters,
            users,
            NullLogger<DispatchCentersController>.Instance);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite")
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
    public async Task CrudAndLinkingFlow_WorksEndToEnd()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);

        var createMain = await controller.Create(new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-main",
            Name = "Main",
            Country = "PL",
            IfMain = true
        });

        Assert.IsType<CreatedResult>(createMain);

        var createWest = await controller.Create(new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-west",
            Name = "West",
            Country = "DE",
            IfMain = false,
            CorrespondingDispatchCenterIds = new List<string> { "dc-main" }
        });

        Assert.IsType<CreatedResult>(createWest);

        var replaceCorresponding = await controller.ReplaceCorresponding("dc-main", new List<string> { "dc-west", "dc-west" });
        var okReplace = Assert.IsType<OkObjectResult>(replaceCorresponding);
        var main = Assert.IsType<DispatchCenter>(okReplace.Value);

        Assert.Single(main.CorrespondingDispatchCenterIds);
        Assert.Contains("dc-west", main.CorrespondingDispatchCenterIds);

        var update = await controller.Update("dc-west", new DispatchCentersController.UpsertDispatchCenterDto
        {
            Name = "West Updated",
            Country = "DE",
            IfMain = false,
            CorrespondingDispatchCenterIds = new List<string> { "dc-main" }
        });

        var okUpdate = Assert.IsType<OkObjectResult>(update);
        var west = Assert.IsType<DispatchCenter>(okUpdate.Value);
        Assert.Equal("West Updated", west.Name);

        var getAll = await controller.GetAll();
        var okAll = Assert.IsType<OkObjectResult>(getAll);
        var all = Assert.IsAssignableFrom<IEnumerable<DispatchCenter>>(okAll.Value);
        Assert.Collection(all,
            a => Assert.Equal("Main", a.Name),
            b => Assert.Equal("West Updated", b.Name));
    }

    [Fact]
    public async Task AssignAndDeleteFlow_CleansUserAndCorrespondingReferences()
    {
        var dispatchRepo = new InMemoryDispatchCentersRepository();
        var usersRepo = new InMemoryUsersRepository();
        var controller = CreateController(dispatchRepo, usersRepo);

        await controller.Create(new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-a",
            Name = "A",
            Country = "PL",
            IfMain = true
        });

        await controller.Create(new DispatchCentersController.UpsertDispatchCenterDto
        {
            Id = "dc-b",
            Name = "B",
            Country = "FR",
            IfMain = false,
            CorrespondingDispatchCenterIds = new List<string> { "dc-a" }
        });

        await usersRepo.UpsertAsync(new ApplicationUser { UserName = "alice", DispatchCenterIds = new List<string>() });

        var assign = await controller.AssignUsers("dc-b", new DispatchCentersController.ManageUsersDto
        {
            UserNames = new List<string> { "alice" }
        });

        var okAssign = Assert.IsType<OkObjectResult>(assign);
        var assignedDc = Assert.IsType<DispatchCenter>(okAssign.Value);
        Assert.Contains("alice", assignedDc.Users);

        var alice = await usersRepo.GetByUserNameAsync("alice");
        Assert.Contains("dc-b", alice.DispatchCenterIds);

        var delete = await controller.Delete("dc-b");
        Assert.IsType<NoContentResult>(delete);

        var deleted = await dispatchRepo.GetByIdAsync("dc-b");
        Assert.Null(deleted);

        var aliceAfter = await usersRepo.GetByUserNameAsync("alice");
        Assert.DoesNotContain("dc-b", aliceAfter.DispatchCenterIds);
    }
}
