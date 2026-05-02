using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;
using Chat.Web.Models;

namespace Chat.Web.Pages.Admin.Users;

[Authorize(Policy = "RequireAdminRole")]
public class UsersIndexModel : PageModel
{
    private readonly IUsersRepository _users;
    private readonly IDispatchCentersRepository _dispatchCenters;

    public UsersIndexModel(IUsersRepository users, IDispatchCentersRepository dispatchCenters)
    {
        _users = users;
        _dispatchCenters = dispatchCenters;
    }

    public IEnumerable<ApplicationUser> Users { get; set; } = Enumerable.Empty<ApplicationUser>();
    public IReadOnlyDictionary<string, string> DispatchCenterNames { get; private set; } = new Dictionary<string, string>();

    public async Task OnGetAsync()
    {
        var usersTask = _users.GetAllAsync();
        var dcsTask = _dispatchCenters.GetAllAsync();
        await Task.WhenAll(usersTask, dcsTask).ConfigureAwait(false);

        Users = await usersTask;
        DispatchCenterNames = (await dcsTask)
            .ToDictionary(d => d.Id, d => d.Name ?? d.Id, System.StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnPostToggleEnabled(string userName)
    {
        var u = await _users.GetByUserNameAsync(userName);
        if (u == null) return RedirectToPage();
        u.Enabled = !u.Enabled;
        await _users.UpsertAsync(u);
        return RedirectToPage();
    }
}
