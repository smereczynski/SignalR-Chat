using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.Users;

[Authorize(Policy = "RequireAdminRole")]
public class UsersEditModel : PageModel
{
    private readonly IUsersRepository _users;

    public UsersEditModel(IUsersRepository users) => _users = users;

    [BindProperty(SupportsGet = true)]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public string FullName { get; set; } = string.Empty;

        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string MobileNumber { get; set; } = string.Empty;

        [EmailAddress]
        public string Upn { get; set; } = string.Empty;

        public string PreferredLanguage { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName))
            return RedirectToPage("Index");

        var user = await _users.GetByUserNameAsync(UserName).ConfigureAwait(false);
        if (user == null)
            return RedirectToPage("Index");

        Input = new InputModel
        {
            FullName = user.FullName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            MobileNumber = user.MobileNumber ?? string.Empty,
            Upn = user.Upn ?? string.Empty,
            PreferredLanguage = user.PreferredLanguage ?? string.Empty,
            Enabled = user.Enabled
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName))
            return RedirectToPage("Index");

        if (!ModelState.IsValid)
            return Page();

        var user = await _users.GetByUserNameAsync(UserName).ConfigureAwait(false);
        if (user == null)
            return RedirectToPage("Index");

        user.FullName = string.IsNullOrWhiteSpace(Input.FullName) ? null : Input.FullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        user.MobileNumber = string.IsNullOrWhiteSpace(Input.MobileNumber) ? null : Input.MobileNumber.Trim();
        user.Upn = string.IsNullOrWhiteSpace(Input.Upn) ? null : Input.Upn.Trim();
        user.PreferredLanguage = string.IsNullOrWhiteSpace(Input.PreferredLanguage) ? null : Input.PreferredLanguage.Trim();
        user.Enabled = Input.Enabled;

        await _users.UpsertAsync(user).ConfigureAwait(false);
        return RedirectToPage("Index");
    }
}
