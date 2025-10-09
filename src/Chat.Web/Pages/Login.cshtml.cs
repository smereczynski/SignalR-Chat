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
                // Already signed in: safely redirect only to local URLs; fallback to /chat
                var dest = string.IsNullOrEmpty(ReturnUrl) ? "/chat" : ReturnUrl;
                if (!Url.IsLocalUrl(dest)) dest = "/chat";
                return LocalRedirect(dest);
            }
            return Page();
        }
    }
}
