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
    public UsersIndexModel(IUsersRepository users) => _users = users;
    public IEnumerable<ApplicationUser> Users { get; set; } = Enumerable.Empty<ApplicationUser>();

    public void OnGet()
    {
        Users = _users.GetAll();
    }

    public IActionResult OnPostToggleEnabled(string userName)
    {
        var u = _users.GetByUserName(userName);
        if (u == null) return RedirectToPage();
        u.Enabled = !u.Enabled;
        _users.Upsert(u);
        return RedirectToPage();
    }
}
