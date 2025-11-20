using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin;

[Authorize(Policy = "RequireAdminRole")]
public class AdminIndexModel : PageModel
{
    public void OnGet() { }
}
