using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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
        public bool IsAdmin { get; set; }
        public bool Enabled { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = new AdminUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            MobileNumber = Input.MobileNumber,
            IsAdmin = Input.IsAdmin,
            Enabled = Input.Enabled,
            Rooms = new List<string>()
        };
        await _users.UpsertAsync(user);
        return RedirectToPage("Index");
    }
}
