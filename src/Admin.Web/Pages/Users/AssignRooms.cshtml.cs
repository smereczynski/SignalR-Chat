using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

public class UsersAssignRoomsModel : PageModel
{
    private readonly IUsersRepository _users;
    private readonly IRoomsRepository _rooms;
    public UsersAssignRoomsModel(IUsersRepository users, IRoomsRepository rooms)
    { _users = users; _rooms = rooms; }

    [BindProperty(SupportsGet = true)]
    public string UserName { get; set; } = string.Empty;
    public List<string> AllRooms { get; set; } = new();
    [BindProperty]
    public List<string> SelectedRooms { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName)) return RedirectToPage("Index");
        var user = await _users.GetAsync(UserName);
        if (user == null) return RedirectToPage("Index");
        var rooms = await _rooms.GetAllNamesAsync();
        AllRooms = rooms.ToList();
        SelectedRooms = user.Rooms;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetAsync(UserName);
        if (user == null) return RedirectToPage("Index");
        user = user with { Rooms = SelectedRooms ?? new List<string>() };
        await _users.UpsertAsync(user);
        return RedirectToPage("Index");
    }
}
