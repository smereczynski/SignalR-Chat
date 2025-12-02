using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;

namespace Chat.Web.Pages.Admin.Users;

[Authorize(Policy = "RequireAdminRole")]
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

    public async Task<IActionResult> OnGet()
    {
        if (string.IsNullOrWhiteSpace(UserName)) return RedirectToPage("Index");
        var user = await _users.GetByUserNameAsync(UserName);
        if (user == null) return RedirectToPage("Index");
        var rooms = await _rooms.GetAllAsync();
        AllRooms = rooms.Select(r => r.Name).ToList();
        SelectedRooms = user.FixedRooms.ToList();
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var user = await _users.GetByUserNameAsync(UserName);
        if (user == null) return RedirectToPage("Index");
        var nextRooms = new HashSet<string>(SelectedRooms ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var prevRooms = new HashSet<string>(user.FixedRooms ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        // Persist user first
        user.FixedRooms = nextRooms.ToList();
        await _users.UpsertAsync(user);

        // Update denormalized helper list in room docs
        foreach (var add in nextRooms.Except(prevRooms))
        {
            await _rooms.AddUserToRoomAsync(add, user.UserName);
        }
        foreach (var rem in prevRooms.Except(nextRooms))
        {
            await _rooms.RemoveUserFromRoomAsync(rem, user.UserName);
        }
        return RedirectToPage("Index");
    }
}
