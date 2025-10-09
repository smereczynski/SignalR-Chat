using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

namespace Chat.Web.Pages
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string ReturnUrl { get; set; } = "/chat";

        public IActionResult OnGet()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                // Already signed in: go to chat
                return LocalRedirect(ReturnUrl ?? "/chat");
            }
            return Page();
        }
    }
}
