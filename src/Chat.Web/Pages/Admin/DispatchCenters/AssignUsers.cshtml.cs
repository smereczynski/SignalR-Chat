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
public class DispatchCentersAssignUsersModel : PageModel
{
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly IUsersRepository _users;
    private readonly Services.DispatchCenterTopologyService _topology;

    public DispatchCentersAssignUsersModel(IDispatchCentersRepository dispatchCenters, IUsersRepository users, Services.DispatchCenterTopologyService topology)
    {
        _dispatchCenters = dispatchCenters;
        _users = users;
        _topology = topology;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public string DispatchCenterName { get; set; } = string.Empty;

    public List<ApplicationUser> AllUsers { get; set; } = new();

    [BindProperty]
    public List<string> SelectedUsers { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return RedirectToPage("Index");

        var dispatchCenter = await _dispatchCenters.GetByIdAsync(Id);
        if (dispatchCenter == null) return RedirectToPage("Index");

        DispatchCenterName = dispatchCenter.Name;
        AllUsers = (await _users.GetAllAsync()).OrderBy(u => u.UserName).ToList();
        SelectedUsers = dispatchCenter.Users?.ToList() ?? new List<string>();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return RedirectToPage("Index");

        var dispatchCenter = await _dispatchCenters.GetByIdAsync(Id);
        if (dispatchCenter == null) return RedirectToPage("Index");

        var selectedSet = new HashSet<string>((SelectedUsers ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(dispatchCenter.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        var toAdd = selectedSet.Except(currentSet).ToList();
        var toRemove = currentSet.Except(selectedSet).ToList();

        if (toAdd.Count > 0)
        {
            await _topology.AssignUsersAsync(Id, toAdd);
        }

        if (toRemove.Count > 0)
        {
            await _topology.RemoveUsersFromDispatchCenterAsync(Id, toRemove);
        }

        return RedirectToPage("Edit", new { id = Id });
    }
}
