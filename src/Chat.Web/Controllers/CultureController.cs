using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Chat.Web.Controllers
{
    /// <summary>
    /// Handles culture/language switching via cookie persistence.
    /// </summary>
    [Route("[controller]/[action]")]
    public class CultureController : Controller
    {
        /// <summary>
        /// Sets the user's preferred culture and persists it in a cookie.
        /// </summary>
        /// <param name="culture">Culture code (e.g., "en", "pl-PL", "de-DE")</param>
        /// <param name="returnUrl">Optional URL to redirect after setting culture (defaults to "/")</param>
        [HttpPost]
        public IActionResult Set(string culture, string returnUrl = "/")
        {
            if (string.IsNullOrWhiteSpace(culture))
            {
                return BadRequest("Culture parameter is required");
            }

            // Set culture cookie with 1-year expiration
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions 
                { 
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps
                }
            );

            // Validate returnUrl to prevent open redirect
            if (!Url.IsLocalUrl(returnUrl))
            {
                returnUrl = "/";
            }

            return LocalRedirect(returnUrl);
        }

        /// <summary>
        /// Gets the current culture (for debugging/API purposes).
        /// </summary>
        [HttpGet]
        public IActionResult Current()
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
            return Ok(new { culture });
        }
    }
}
