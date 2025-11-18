using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Chat.Web.Options;
using System.Threading.Tasks;

namespace Chat.Web.Pages.Login
{
    /// <summary>
    /// Entra ID authentication challenge page. Redirects user to Microsoft login for OpenID Connect authentication.
    /// After successful authentication, the OnTokenValidated event in Startup.cs handles UPN validation and user creation.
    /// </summary>
    [AllowAnonymous]
    public class EntraIdModel : PageModel
    {
        private readonly ILogger<EntraIdModel> _logger;
        private readonly EntraIdOptions _entraIdOptions;

        public EntraIdModel(
            ILogger<EntraIdModel> logger,
            IOptions<EntraIdOptions> entraIdOptions)
        {
            _logger = logger;
            _entraIdOptions = entraIdOptions.Value;
        }

        [BindProperty(SupportsGet = true)]
        public string ReturnUrl { get; set; } = "/chat";

        public async Task<IActionResult> OnGetAsync()
        {
            // If Entra ID is not configured, redirect back to login page
            if (!_entraIdOptions.IsEnabled)
            {
                _logger.LogWarning("Entra ID authentication attempted but not configured (missing ClientId or ClientSecret)");
                return RedirectToPage("/Login", new { error = "entra_id_not_configured" });
            }

            // Validate ReturnUrl (security: prevent open redirect)
            if (!string.IsNullOrEmpty(ReturnUrl) && !Url.IsLocalUrl(ReturnUrl))
            {
                _logger.LogWarning(
                    "Invalid ReturnUrl rejected during Entra ID login: {ReturnUrl}",
                    Chat.Web.Utilities.LogSanitizer.Sanitize(ReturnUrl));
                ReturnUrl = "/chat";
            }

            // Challenge Entra ID authentication scheme
            _logger.LogInformation("Initiating Entra ID authentication challenge");
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = ReturnUrl ?? "/chat"
                },
                "EntraId");
        }
    }
}
