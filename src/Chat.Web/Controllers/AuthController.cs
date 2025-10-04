using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    /// <summary>
    /// Implements a lightweight OTP-based authentication flow (start + verify) with cookie issuance.
    /// Rate limiting applied to mitigate brute-force and enumeration attempts.
    /// </summary>
    public class AuthController : ControllerBase
    {
        private readonly IUsersRepository _users;
        private readonly IOtpStore _otpStore;
    private readonly IOtpSender _otpSender;
    private readonly Services.IInProcessMetrics _metrics;

        /// <summary>
        /// DI constructor for auth endpoints.
        /// </summary>
        public AuthController(IUsersRepository users, IOtpStore otpStore, IOtpSender otpSender, Services.IInProcessMetrics metrics)
        {
            _users = users;
            _otpStore = otpStore;
            _otpSender = otpSender;
            _metrics = metrics;
        }

        /// <summary>
        /// Begins OTP flow: validates user existence, generates and stores a code, sends via configured sender.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("start")]
        [EnableRateLimiting("AuthEndpoints")]
        public async Task<IActionResult> Start([FromBody] StartRequest req)
        {
            var user = _users.GetByUserName(req.UserName);
            if (user == null) return Unauthorized();

            var code = new Random().Next(100000, 999999).ToString();
            await _otpStore.SetAsync(req.UserName, code, TimeSpan.FromMinutes(5));
            await _otpSender.SendAsync(req.UserName, req.Destination ?? req.UserName, code);
            _metrics.IncOtpRequests();
            return Ok();
        }

        /// <summary>
        /// Verifies the supplied OTP code, issues auth cookie on success and removes stored code.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("verify")]
        [EnableRateLimiting("AuthEndpoints")]
        public async Task<IActionResult> Verify([FromBody] VerifyRequest req)
        {
            var expected = await _otpStore.GetAsync(req.UserName);
            if (string.IsNullOrEmpty(expected) || expected != req.Code)
                return Unauthorized();

            await _otpStore.RemoveAsync(req.UserName);
            _metrics.IncOtpVerifications();

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, req.UserName)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });
            return Ok();
        }

    /// <summary>
    /// Signs the current user out (invalidates authentication cookie).
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
        }

    /// <summary>
    /// Returns lightweight profile information for the authenticated user.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
        {
            var userName = User?.Identity?.Name;
            if (string.IsNullOrEmpty(userName)) return Unauthorized();
            var user = _users.GetByUserName(userName);
            if (user == null) return Unauthorized();
            return Ok(new { userName = user.UserName, fullName = user.FullName, avatar = user.Avatar });
        }

        /// <summary>
        /// Request body for OTP start (Destination optional override, defaults to username when not provided).
        /// </summary>
        public class StartRequest { public string UserName { get; set; } public string Destination { get; set; } }
        /// <summary>
        /// Request body for OTP verification.
        /// </summary>
        public class VerifyRequest { public string UserName { get; set; } public string Code { get; set; } }
    }
}
