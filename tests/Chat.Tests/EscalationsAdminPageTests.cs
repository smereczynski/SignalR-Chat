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
    public async Task OnGet_CombinedFilters_ReturnOnlyMatchingEscalation()
    {
        var repo = new InMemoryEscalationsRepository();
        var matching = MakeEscalation("e1", EscalationStatus.Escalated, EscalationTriggerType.Manual, "pair:A::B");
        matching.SourceDispatchCenterId = "dc-a";
        await repo.CreateAsync(matching);
        await repo.CreateAsync(MakeEscalation("e2", EscalationStatus.Escalated, EscalationTriggerType.Automatic, "pair:A::B"));
        await repo.CreateAsync(MakeEscalation("e3", EscalationStatus.Resolved, EscalationTriggerType.Manual, "pair:A::B"));
        await repo.CreateAsync(MakeEscalation("e4", EscalationStatus.Escalated, EscalationTriggerType.Manual, "pair:B::C"));

        var page = BuildPage(repo);
        await page.OnGetAsync(roomName: "pair:A::B", status: "Escalated", triggerType: "Manual", sourceDispatchCenterId: "dc-a", targetDispatchCenterId: null);

        Assert.Single(page.Escalations);
        Assert.Equal("e1", page.Escalations[0].Id);
    }

    [Fact]
    public async Task OnGet_DispatchCenterFilter_KeepsStableKnownRoomsAndNames()
    {
        var repo = new InMemoryEscalationsRepository();
        var dcs = new InMemoryDispatchCentersRepository();
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-a", Name = "Alpha" });
        await dcs.UpsertAsync(new DispatchCenter { Id = "dc-b", Name = "Beta" });

        var e1 = MakeEscalation("e1", EscalationStatus.Escalated);
        e1.SourceDispatchCenterId = "dc-a";
        e1.RoomName = "pair:A::B";
        var e2 = MakeEscalation("e2", EscalationStatus.Escalated);
        e2.SourceDispatchCenterId = "dc-b";
        e2.RoomName = "pair:B::C";
        await repo.CreateAsync(e1);
        await repo.CreateAsync(e2);

        var page = BuildPage(repo, dcs);
        await page.OnGetAsync(roomName: null, status: null, triggerType: null, sourceDispatchCenterId: "dc-a", targetDispatchCenterId: null);

        Assert.Single(page.Escalations);
        Assert.Equal("e1", page.Escalations[0].Id);
        Assert.Equal("Alpha", page.DispatchCenterNames["dc-a"]);
        Assert.Contains("pair:A::B", page.KnownRooms);
        Assert.Contains("pair:B::C", page.KnownRooms);
    }
}
