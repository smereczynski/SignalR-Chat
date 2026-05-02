using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.Rooms;

[Authorize(Policy = "RequireAdminRole")]
public class RoomsIndexModel : PageModel
{
    private readonly IRoomsRepository _rooms;
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly DispatchCenterTopologyService _topology;

    public RoomsIndexModel(IRoomsRepository rooms, IDispatchCentersRepository dispatchCenters, DispatchCenterTopologyService topology)
    {
        _rooms = rooms;
        _dispatchCenters = dispatchCenters;
        _topology = topology;
    }

    public IReadOnlyList<Room> Rooms { get; private set; } = new List<Room>();
    public IReadOnlyDictionary<string, string> DispatchCenterNames { get; private set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, DispatchCenter> DispatchCenters { get; private set; } = new Dictionary<string, DispatchCenter>();

    public async Task OnGetAsync()
    {
        var roomsTask = _rooms.GetAllAsync();
        var dcsTask = _dispatchCenters.GetAllAsync();

        await Task.WhenAll(roomsTask, dcsTask).ConfigureAwait(false);

        Rooms = (await roomsTask).OrderBy(r => r.Name).ToList();
        DispatchCenters = (await dcsTask).ToDictionary(d => d.Id, d => d, System.StringComparer.OrdinalIgnoreCase);
        DispatchCenterNames = DispatchCenters.ToDictionary(kv => kv.Key, kv => kv.Value.Name ?? kv.Value.Id, System.StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostSyncAsync()
    {
        await _topology.SyncRoomsAsync().ConfigureAwait(false);
        TempData["SyncSuccess"] = true;
        return RedirectToPage();
    }
}
