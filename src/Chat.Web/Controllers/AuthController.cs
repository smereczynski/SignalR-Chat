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
using System.Text.Json;

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
        /// Returns a lightweight list of users that can log in (for dropdown population). Public to simplify demo UX.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("users")]
        public IActionResult Users()
        {
            var users = _users.GetAll();
            var list = System.Linq.Enumerable.Select(users, u => new { userName = u.UserName, fullName = u.FullName });
            // Manual serialization to avoid test harness PipeWriter issue.
            var json = JsonSerializer.Serialize(list);
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
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

            // Reuse existing unexpired code to avoid flooding destinations; otherwise create a new one.
            var existing = await _otpStore.GetAsync(req.UserName);
            var code = existing;
            if (string.IsNullOrEmpty(existing))
            {
                code = new Random().Next(100000, 999999).ToString();
                await _otpStore.SetAsync(req.UserName, code, TimeSpan.FromMinutes(5));
            }
            // Send to email and mobile when available (fire-and-forget pattern after first send success)
            // Primary channel: email (if present) otherwise mobile else fallback to username.
            var primary = user.Email ?? user.MobileNumber ?? req.UserName;
            await _otpSender.SendAsync(req.UserName, primary, code);
            if (!string.IsNullOrWhiteSpace(user.MobileNumber) && user.MobileNumber != primary)
            {
                _ = _otpSender.SendAsync(req.UserName, user.MobileNumber, code);
            }
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
            var payload = new { userName = user.UserName, fullName = user.FullName, avatar = user.Avatar };
            // WORKAROUND: Manual serialization to avoid PipeWriter UnflushedBytes issue in test harness.
            var json = JsonSerializer.Serialize(payload);
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }

        /// <summary>
        /// Request body for OTP start (Destination optional override, defaults to username when not provided).
        /// </summary>
    public class StartRequest { public string UserName { get; set; } }
        /// <summary>
        /// Request body for OTP verification.
        /// </summary>
        public class VerifyRequest { public string UserName { get; set; } public string Code { get; set; } }
    }
}
