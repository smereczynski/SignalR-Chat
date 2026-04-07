using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.Users;

[Authorize(Policy = "RequireAdminRole")]
public class UsersAssignDispatchCenterModel : PageModel
{
    private readonly IUsersRepository _users;
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly Services.DispatchCenterTopologyService _topology;

    public UsersAssignDispatchCenterModel(
        IUsersRepository users,
        IDispatchCentersRepository dispatchCenters,
        Services.DispatchCenterTopologyService topology)
    {
        _users = users;
        _dispatchCenters = dispatchCenters;
        _topology = topology;
    }

    [BindProperty(SupportsGet = true)]
    public string UserName { get; set; } = string.Empty;

    public List<Models.DispatchCenter> DispatchCenters { get; set; } = new();

    [BindProperty]
    public string SelectedDispatchCenterId { get; set; } = string.Empty;

    public async Task<IActionResult> OnGet()
    {
        if (string.IsNullOrWhiteSpace(UserName)) return RedirectToPage("Index");

        var user = await _users.GetByUserNameAsync(UserName);
        if (user == null) return RedirectToPage("Index");

        DispatchCenters = (await _dispatchCenters.GetAllAsync()).OrderBy(x => x.Name).ToList();
        SelectedDispatchCenterId = user.DispatchCenterId ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var user = await _users.GetByUserNameAsync(UserName);
        if (user == null) return RedirectToPage("Index");

        if (string.IsNullOrWhiteSpace(SelectedDispatchCenterId))
        {
            await _topology.RemoveUserFromDispatchCenterAsync(user.DispatchCenterId, user.UserName);
            return RedirectToPage("Index");
        }

        var dispatchCenter = await _dispatchCenters.GetByIdAsync(SelectedDispatchCenterId);
        if (dispatchCenter == null)
        {
            return RedirectToPage("Index");
        }

        await _topology.AssignUserAsync(SelectedDispatchCenterId, user.UserName);
        return RedirectToPage("Index");
    }
}
