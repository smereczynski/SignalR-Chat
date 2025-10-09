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
using Microsoft.Extensions.Logging;

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
    private readonly IOtpHasher _otpHasher;
    private readonly Microsoft.Extensions.Options.IOptions<Chat.Web.Options.OtpOptions> _otpOptions;
    private readonly IOtpSender _otpSender;
    private readonly Services.IInProcessMetrics _metrics;
    private readonly Microsoft.Extensions.Logging.ILogger<AuthController> _logger;

        /// <summary>
        /// DI constructor for auth endpoints.
        /// </summary>
        public AuthController(IUsersRepository users, IOtpStore otpStore, IOtpSender otpSender, Services.IInProcessMetrics metrics, IOtpHasher otpHasher, Microsoft.Extensions.Options.IOptions<Chat.Web.Options.OtpOptions> otpOptions, Microsoft.Extensions.Logging.ILogger<AuthController> logger)
        {
            _users = users;
            _otpStore = otpStore;
            _otpSender = otpSender;
            _metrics = metrics;
            _otpHasher = otpHasher;
            _otpOptions = otpOptions;
            _logger = logger;
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

            // If a non-expired code already exists, do NOT send again within TTL to avoid duplicate emails.
            var existing = await _otpStore.GetAsync(req.UserName);
            string code = null;
            var hashingEnabled = _otpOptions.Value?.HashingEnabled ?? true;
            if (!string.IsNullOrEmpty(existing))
            {
                // A code is already active; short-circuit to prevent repeated sends within TTL.
                _metrics.IncOtpRequests();
                return Accepted();
            }
            // Create and store a fresh code
            code = new Random().Next(100000, 999999).ToString();
            var toStore = hashingEnabled ? _otpHasher.Hash(req.UserName, code) : code;
            await _otpStore.SetAsync(req.UserName, toStore, TimeSpan.FromMinutes(5));
            // Prepare to send via all available channels (email + SMS) without preference.
            // Collect unique destinations to avoid duplicate sends if username equals email, etc.
            var destinations = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(user.Email)) destinations.Add(user.Email);
            if (!string.IsNullOrWhiteSpace(user.MobileNumber)) destinations.Add(user.MobileNumber);
            if (destinations.Count == 0 && !string.IsNullOrWhiteSpace(req.UserName)) destinations.Add(req.UserName);

            // Dispatch background sends to avoid blocking the request on external provider cold-starts
            void FireAndForget(Func<Task> op, string dest)
            {
                _ = Task.Run(async () =>
                {
                    var sanitizedUserName = req.UserName.Replace("\r", "").Replace("\n", "");
                    try { await op().ConfigureAwait(false); _logger.LogInformation("OTP dispatched to {Destination} for {User}", dest, sanitizedUserName); }
                    catch (Exception ex) { _logger.LogWarning(ex, "OTP dispatch failed to {Destination} for {User}", dest, sanitizedUserName); }
                });
            }
            foreach (var dest in destinations)
            {
                FireAndForget(() => _otpSender.SendAsync(req.UserName, dest, code), dest);
            }
            _metrics.IncOtpRequests();
            // Indicate asynchronous processing
            return Accepted();
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
            if (string.IsNullOrEmpty(expected)) return Unauthorized();
            bool ok;
            if (expected.StartsWith("OtpHash:", StringComparison.Ordinal))
            {
                var result = _otpHasher.Verify(req.UserName, req.Code, expected);
                ok = result.IsMatch;
            }
            else
            {
                // Legacy plaintext path (short-lived during migration)
                ok = FixedTimeEquals(expected, req.Code);
            }
            if (!ok) return Unauthorized();

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

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var ba = System.Text.Encoding.UTF8.GetBytes(a);
            var bb = System.Text.Encoding.UTF8.GetBytes(b);
            var equal = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
            Array.Clear(ba, 0, ba.Length);
            Array.Clear(bb, 0, bb.Length);
            return equal;
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
