using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Pages.Admin.Escalations;
using Chat.Web.Repositories;
using Xunit;

namespace Chat.Tests;

public class EscalationsAdminPageTests
{
    private static EscalationsIndexModel BuildPage(
        InMemoryEscalationsRepository? escalations = null,
        InMemoryDispatchCentersRepository? dcs = null)
    {
        return new EscalationsIndexModel(
            escalations ?? new InMemoryEscalationsRepository(),
            dcs ?? new InMemoryDispatchCentersRepository());
    }

    private static Escalation MakeEscalation(string id, EscalationStatus status, EscalationTriggerType triggerType = EscalationTriggerType.Automatic, string roomName = "pair:A::B")
        => new Escalation
        {
            Id = id,
            RoomName = roomName,
            Status = status,
            TriggerType = triggerType,
            CreatedAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow,
            SourceDispatchCenterId = "dc-a",
            TargetDispatchCenterId = "dc-b"
        };

    [Fact]
    public async Task OnGet_NoFilters_ReturnsAllRecentEscalations()
    {
        var repo = new InMemoryEscalationsRepository();
        await repo.CreateAsync(MakeEscalation("e1", EscalationStatus.Escalated));
        await repo.CreateAsync(MakeEscalation("e2", EscalationStatus.Resolved));
        await repo.CreateAsync(MakeEscalation("e3", EscalationStatus.Cancelled));

        var page = BuildPage(repo);
        await page.OnGetAsync(roomName: null, status: null, triggerType: null, sourceDispatchCenterId: null, targetDispatchCenterId: null);

        Assert.Equal(3, page.Escalations.Count);
    }

    [Fact]
    public async Task OnGet_StatusFilter_ReturnsOnlyMatchingEscalations()
    {
        var repo = new InMemoryEscalationsRepository();
        await repo.CreateAsync(MakeEscalation("e1", EscalationStatus.Escalated));
        await repo.CreateAsync(MakeEscalation("e2", EscalationStatus.Resolved));

        var page = BuildPage(repo);
        await page.OnGetAsync(roomName: null, status: "Escalated", triggerType: null, sourceDispatchCenterId: null, targetDispatchCenterId: null);

        Assert.Single(page.Escalations);
        Assert.Equal("e1", page.Escalations[0].Id);
    }

    [Fact]
    public async Task OnGet_RoomNameFilter_ReturnsOnlyMatchingRoom()
    {
        var repo = new InMemoryEscalationsRepository();
        await repo.CreateAsync(MakeEscalation("e1", EscalationStatus.Escalated, roomName: "pair:A::B"));
        await repo.CreateAsync(MakeEscalation("e2", EscalationStatus.Escalated, roomName: "pair:B::C"));

        var page = BuildPage(repo);
        await page.OnGetAsync(roomName: "pair:A::B", status: null, triggerType: null, sourceDispatchCenterId: null, targetDispatchCenterId: null);

        Assert.Single(page.Escalations);
        Assert.Equal("e1", page.Escalations[0].Id);
    }

    [Fact]
    public async Task OnGet_TriggerTypeFilter_ReturnsOnlyMatchingTrigger()
    {
        var repo = new InMemoryEscalationsRepository();
        await repo.CreateAsync(MakeEscalation("e1", EscalationStatus.Escalated, EscalationTriggerType.Automatic));
        await repo.CreateAsync(MakeEscalation("e2", EscalationStatus.Escalated, EscalationTriggerType.Manual));

        var page = BuildPage(repo);
        await page.OnGetAsync(roomName: null, status: null, triggerType: "Manual", sourceDispatchCenterId: null, targetDispatchCenterId: null);

        Assert.Single(page.Escalations);
        Assert.Equal("e2", page.Escalations[0].Id);
    }

    [Fact]
    public async Task OnGet_SourceDispatchCenterFilter_ReturnsOnlyMatchingSource()
    {
        var repo = new InMemoryEscalationsRepository();
        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-a", Name = "Alpha" });
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-b", Name = "Beta" });

        var e1 = MakeEscalation("e1", EscalationStatus.Escalated);
        e1.SourceDispatchCenterId = "dc-a";
        var e2 = MakeEscalation("e2", EscalationStatus.Escalated);
        e2.SourceDispatchCenterId = "dc-b";
        await repo.CreateAsync(e1);
        await repo.CreateAsync(e2);

        var page = BuildPage(repo, dcs);
        await page.OnGetAsync(roomName: null, status: null, triggerType: null, sourceDispatchCenterId: "dc-a", targetDispatchCenterId: null);

        Assert.Single(page.Escalations);
        Assert.Equal("e1", page.Escalations[0].Id);
    }

    [Fact]
    public async Task OnGet_TargetDispatchCenterFilter_ReturnsOnlyMatchingTarget()
    {
        var repo = new InMemoryEscalationsRepository();
        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-a", Name = "Alpha" });
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-b", Name = "Beta" });

        var e1 = MakeEscalation("e1", EscalationStatus.Escalated);
        e1.TargetDispatchCenterId = "dc-b";
        var e2 = MakeEscalation("e2", EscalationStatus.Escalated);
        e2.TargetDispatchCenterId = "dc-a";
        await repo.CreateAsync(e1);
        await repo.CreateAsync(e2);

        var page = BuildPage(repo, dcs);
        await page.OnGetAsync(roomName: null, status: null, triggerType: null, sourceDispatchCenterId: null, targetDispatchCenterId: "dc-b");

        Assert.Single(page.Escalations);
        Assert.Equal("e1", page.Escalations[0].Id);
    }
}
