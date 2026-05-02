using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin;

[Authorize(Policy = "RequireAdminRole")]
public class AdminIndexModel : PageModel
{
    private readonly IUsersRepository _users;
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly IRoomsRepository _rooms;
    private readonly IEscalationsRepository _escalations;
    private readonly DispatchCenterTopologyService _topology;

    public AdminIndexModel(
        IUsersRepository users,
        IDispatchCentersRepository dispatchCenters,
        IRoomsRepository rooms,
        IEscalationsRepository escalations,
        DispatchCenterTopologyService topology)
    {
        _users = users;
        _dispatchCenters = dispatchCenters;
        _rooms = rooms;
        _escalations = escalations;
        _topology = topology;
    }

    public DashboardStats Stats { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var usersTask = _users.GetAllAsync();
        var dcsTask = _dispatchCenters.GetAllAsync();
        var roomsTask = _rooms.GetAllAsync();
        var escalationsTask = _escalations.GetRecentAsync(take: 500);

        await Task.WhenAll(usersTask, dcsTask, roomsTask, escalationsTask).ConfigureAwait(false);

        var allUsers = (await usersTask).ToList();
        var allDcs = (await dcsTask).ToList();
        var allRooms = (await roomsTask).ToList();
        var recentEscalations = (await escalationsTask).ToList();

        Stats = new DashboardStats
        {
            UsersWithoutDispatchCenter = allUsers.Count(u => string.IsNullOrWhiteSpace(u.DispatchCenterId)),
            DispatchCentersWithoutOfficers = allDcs.Count(d =>
                d.OfficerUserNames == null || !d.OfficerUserNames.Any(x => !string.IsNullOrWhiteSpace(x))),
            DispatchCentersWithoutPairs = allDcs.Count(d =>
                d.CorrespondingDispatchCenterIds == null || !d.CorrespondingDispatchCenterIds.Any(x => !string.IsNullOrWhiteSpace(x))),
            InactivePairRooms = allRooms.Count(r => !r.IsActive),
            OpenEscalations = recentEscalations.Count(e =>
                e.Status == EscalationStatus.Scheduled || e.Status == EscalationStatus.Escalated)
        };
    }

    public async Task<IActionResult> OnPostSyncAsync()
    {
        await _topology.SyncRoomsAsync().ConfigureAwait(false);
        TempData["SyncSuccess"] = true;
        return RedirectToPage();
    }
}

public class DashboardStats
{
    public int UsersWithoutDispatchCenter { get; set; }
    public int DispatchCentersWithoutOfficers { get; set; }
    public int DispatchCentersWithoutPairs { get; set; }
    public int InactivePairRooms { get; set; }
    public int OpenEscalations { get; set; }

    public bool HasWarnings =>
        UsersWithoutDispatchCenter > 0 ||
        DispatchCentersWithoutOfficers > 0 ||
        DispatchCentersWithoutPairs > 0 ||
        InactivePairRooms > 0 ||
        OpenEscalations > 0;
}
