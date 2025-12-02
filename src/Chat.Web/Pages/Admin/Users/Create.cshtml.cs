using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;
using Chat.Web.Models;

namespace Chat.Web.Pages.Admin.Users;

[Authorize(Policy = "RequireAdminRole")]
public class UsersCreateModel : PageModel
{
    private readonly IUsersRepository _users;
    public UsersCreateModel(IUsersRepository users) => _users = users;

    public class InputModel
    {
        [Required]
        public string UserName { get; set; } = string.Empty;
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string MobileNumber { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
        // No initialization required - displays empty form for user creation
        // Actual user creation happens in OnPost handler
    }

    public async Task<IActionResult> OnPost()
    {
        if (!ModelState.IsValid) return Page();
        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            MobileNumber = Input.MobileNumber,
            Enabled = Input.Enabled,
            FixedRooms = new List<string>()
        };
        await _users.UpsertAsync(user);
        return RedirectToPage("Index");
    }
}
