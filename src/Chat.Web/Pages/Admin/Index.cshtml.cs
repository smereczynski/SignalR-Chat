using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin;

[Authorize(Policy = "RequireAdminRole")]
public class AdminIndexModel : PageModel
{
    public void OnGet()
    {
        // No server-side data loading required - page displays static dashboard content
        // and retrieves user claims directly from HttpContext.User in the view
    }
}
