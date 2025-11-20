using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;

namespace Chat.Web.Pages.Admin.Rooms;

[Authorize(Policy = "RequireAdminRole")]
public class RoomsCreateModel : PageModel
{
    private readonly IRoomsRepository _rooms;
    public RoomsCreateModel(IRoomsRepository rooms) => _rooms = rooms;
    [BindProperty, Required]
    public string Name { get; set; } = string.Empty;
    public void OnGet() { }
    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        // Note: Room creation not supported in current repository interface
        // This would require adding a CreateRoom method to IRoomsRepository
        return RedirectToPage("Index");
    }
}
