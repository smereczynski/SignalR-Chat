using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

public class RoomsCreateModel : PageModel
{
    private readonly IRoomsRepository _rooms;
    public RoomsCreateModel(IRoomsRepository rooms) => _rooms = rooms;
    [BindProperty, Required]
    public string Name { get; set; } = string.Empty;
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        await _rooms.CreateAsync(Name);
        return RedirectToPage("Index");
    }
}
