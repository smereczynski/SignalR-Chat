using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.Escalations;

[Authorize(Policy = "RequireAdminRole")]
public class EscalationsIndexModel : PageModel
{
    private readonly IEscalationsRepository _escalations;
    private readonly IDispatchCentersRepository _dispatchCenters;

    public EscalationsIndexModel(IEscalationsRepository escalations, IDispatchCentersRepository dispatchCenters)
    {
        _escalations = escalations;
        _dispatchCenters = dispatchCenters;
    }

    public string RoomNameFilter { get; private set; }
    public EscalationStatus? StatusFilter { get; private set; }
    public EscalationTriggerType? TriggerTypeFilter { get; private set; }
    public string SourceDispatchCenterIdFilter { get; private set; }
    public string TargetDispatchCenterIdFilter { get; private set; }

    public IReadOnlyList<Escalation> Escalations { get; private set; } = new List<Escalation>();
    public IReadOnlyDictionary<string, string> DispatchCenterNames { get; private set; } = new Dictionary<string, string>();
    public IReadOnlyList<string> KnownRooms { get; private set; } = new List<string>();

    public async Task OnGetAsync(string roomName, string status, string triggerType, string sourceDispatchCenterId, string targetDispatchCenterId)
    {
        RoomNameFilter = roomName;
        StatusFilter = System.Enum.TryParse<EscalationStatus>(status, ignoreCase: true, out var s) ? s : null;
        TriggerTypeFilter = System.Enum.TryParse<EscalationTriggerType>(triggerType, ignoreCase: true, out var t) ? t : null;
        SourceDispatchCenterIdFilter = sourceDispatchCenterId;
        TargetDispatchCenterIdFilter = targetDispatchCenterId;

        var escalationsTask = _escalations.GetRecentAsync(take: 100, status: StatusFilter, roomName: RoomNameFilter);
        var dcsTask = _dispatchCenters.GetAllAsync();

        await System.Threading.Tasks.Task.WhenAll(escalationsTask, dcsTask).ConfigureAwait(false);

        var all = (await escalationsTask).ToList();

        if (TriggerTypeFilter.HasValue)
            all = all.Where(e => e.TriggerType == TriggerTypeFilter.Value).ToList();

        var allBeforeDcFilter = all;

        if (!string.IsNullOrWhiteSpace(SourceDispatchCenterIdFilter))
            all = all.Where(e => string.Equals(e.SourceDispatchCenterId, SourceDispatchCenterIdFilter, System.StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(TargetDispatchCenterIdFilter))
            all = all.Where(e => string.Equals(e.TargetDispatchCenterId, TargetDispatchCenterIdFilter, System.StringComparison.OrdinalIgnoreCase)).ToList();

        Escalations = all;
        DispatchCenterNames = (await dcsTask)
            .ToDictionary(d => d.Id, d => d.Name ?? d.Id, System.StringComparer.OrdinalIgnoreCase);

        // Collect distinct room names from pre-DC-filter results so the dropdown is stable when DC filters are active
        KnownRooms = allBeforeDcFilter.Select(e => e.RoomName)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r)
            .ToList();
    }
}
