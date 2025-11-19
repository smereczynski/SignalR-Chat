#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Chat.Web.Options;

namespace Chat.Web.Pages
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly EntraIdOptions _entraIdOptions;

        public LoginModel(IOptions<EntraIdOptions> entraIdOptions)
        {
            _entraIdOptions = entraIdOptions.Value;
        }

        [BindProperty(SupportsGet = true)]
        public string ReturnUrl { get; set; } = "/chat";

        [BindProperty(SupportsGet = true)]
        public string? Reason { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Error { get; set; }

        public bool EntraIdEnabled => _entraIdOptions.IsEnabled;
        public bool OtpFallbackAllowed => _entraIdOptions.Fallback?.OtpForUnauthorizedUsers == true;

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
