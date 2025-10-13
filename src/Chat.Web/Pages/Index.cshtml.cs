using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Pages
{
    // Landing page is anonymous; auth enforced on /chat via [Authorize] and cookie middleware.
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return LocalRedirect("/chat");
            }
            return Page();
        }
    }
}
