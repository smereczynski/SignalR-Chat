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

        var allUsers = (await _users.GetAllAsync()).ToList();
        var selectedSet = new HashSet<string>((SelectedUsers ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(dispatchCenter.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var userName in selectedSet.Except(currentSet))
        {
            await _dispatchCenters.AssignUserAsync(Id, userName);

            var user = allUsers.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (user == null) continue;

            await _topology.AssignUserAsync(Id, userName);
        }

        foreach (var userName in currentSet.Except(selectedSet))
        {
            await _dispatchCenters.UnassignUserAsync(Id, userName);

            var user = allUsers.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (user == null) continue;

            await _topology.RemoveUserFromDispatchCenterAsync(Id, userName);
        }

        return RedirectToPage("Edit", new { id = Id });
    }
}
