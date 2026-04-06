using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.DispatchCenters;

[Authorize(Policy = "RequireAdminRole")]
public class DispatchCentersIndexModel : PageModel
{
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly Services.DispatchCenterTopologyService _topology;

    public DispatchCentersIndexModel(IDispatchCentersRepository dispatchCenters, Services.DispatchCenterTopologyService topology)
    {
        _dispatchCenters = dispatchCenters;
        _topology = topology;
    }

    public IEnumerable<DispatchCenter> DispatchCenters { get; set; } = Enumerable.Empty<DispatchCenter>();

    public Dictionary<string, string> DispatchCenterLabelsById { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task OnGetAsync()
    {
        var allDispatchCenters = (await _dispatchCenters.GetAllAsync()).ToList();
        DispatchCenters = allDispatchCenters.OrderBy(d => d.Name);
        DispatchCenterLabelsById = allDispatchCenters.ToDictionary(
            d => d.Id,
            d => $"{d.Name} ({d.Country})",
            StringComparer.OrdinalIgnoreCase);
    }

    public string FormatCorrespondingDispatchCenters(ICollection<string> correspondingDispatchCenterIds)
    {
        if (correspondingDispatchCenterIds is null || correspondingDispatchCenterIds.Count == 0)
        {
            return string.Empty;
        }

        var labels = correspondingDispatchCenterIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => DispatchCenterLabelsById.TryGetValue(id, out var label) ? label : id)
            .ToList();

        return string.Join(", ", labels);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToPage();

        await _topology.DeleteDispatchCenterAsync(id);
        return RedirectToPage();
    }
}
