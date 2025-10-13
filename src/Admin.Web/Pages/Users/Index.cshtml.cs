using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

public class UsersIndexModel : PageModel
{
    private readonly IUsersRepository _users;
    public UsersIndexModel(IUsersRepository users) => _users = users;
    public IEnumerable<AdminUser> Users { get; set; } = Enumerable.Empty<AdminUser>();

    public async Task OnGetAsync()
    {
        Users = await _users.GetAllAsync();
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(string userName)
    {
        var u = await _users.GetAsync(userName);
        if (u == null) return RedirectToPage();
        u = u with { Enabled = !u.Enabled };
        await _users.UpsertAsync(u);
        return RedirectToPage();
    }
}
