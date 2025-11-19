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
using Microsoft.Extensions.Options;
using Chat.Web.Options;

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
        private readonly IOptions<EntraIdOptions> _entraOptions;

        /// <summary>
        /// DI constructor for auth endpoints.
        /// </summary>
        public AuthController(IUsersRepository users, IOtpStore otpStore, IOtpSender otpSender, Services.IInProcessMetrics metrics, IOtpHasher otpHasher, Microsoft.Extensions.Options.IOptions<Chat.Web.Options.OtpOptions> otpOptions, Microsoft.Extensions.Logging.ILogger<AuthController> logger, IOptions<EntraIdOptions> entraOptions)
        {
            _users = users;
            _otpStore = otpStore;
            _otpSender = otpSender;
            _metrics = metrics;
            _otpHasher = otpHasher;
            _otpOptions = otpOptions;
            _logger = logger;
            _entraOptions = entraOptions;
        }

        /// <summary>
    /// Returns a lightweight list of users that can log in (for dropdown population). Public to simplify sign-in UX.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("users")]
        public IActionResult Users()
        {
            var users = _users.GetAll();
            // Only expose enabled users to the login dropdown
            var list = System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(users, u => (u?.Enabled) != false), u => new { userName = u.UserName, fullName = u.FullName });
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
            if (user == null)
            {
                _logger.LogWarning("OTP Start: User not found {UserName}", Chat.Web.Utilities.LogSanitizer.Sanitize(req.UserName));
                return Unauthorized();
            }
            if (user.Enabled == false)
            {
                _logger.LogWarning("OTP Start: User disabled {UserName}", Chat.Web.Utilities.LogSanitizer.Sanitize(req.UserName));
                return Unauthorized();
            }

            // Check if a code already exists - allow resend after 60 seconds to improve UX
            var existing = await _otpStore.GetAsync(req.UserName);
            string code = null;
            var hashingEnabled = _otpOptions.Value?.HashingEnabled ?? true;
            
            // Generate new code (either first send or resend)
            code = new Random().Next(100000, 999999).ToString();
            var toStore = hashingEnabled ? _otpHasher.Hash(req.UserName, code) : code;
            await _otpStore.SetAsync(req.UserName, toStore, TimeSpan.FromMinutes(5));
            
            // Prepare to send via all available channels (email + SMS) in parallel.
            // Collect unique destinations to avoid duplicate sends if username equals email, etc.
            var destinations = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(user.Email)) destinations.Add(user.Email);
            if (!string.IsNullOrWhiteSpace(user.MobileNumber)) destinations.Add(user.MobileNumber);
            if (destinations.Count == 0 && !string.IsNullOrWhiteSpace(req.UserName)) destinations.Add(req.UserName);

            var sanitizedUserName = req.UserName.Replace("\r", "").Replace("\n", "");
            
            // Send in parallel with timeout to coordinate email and SMS delivery
            var sendTasks = new System.Collections.Generic.List<Task>();
            foreach (var dest in destinations)
            {
                var capturedDest = dest; // Capture for closure
                sendTasks.Add(Task.Run(async () =>
                {
                    try 
                    { 
                        await _otpSender.SendAsync(req.UserName, capturedDest, code).ConfigureAwait(false);
                        _logger.LogInformation("OTP dispatched for {User} to destination", sanitizedUserName);
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogWarning(ex, "OTP dispatch failed for {User} to destination", sanitizedUserName);
                    }
                }));
            }

            // Wait for all sends with a 10-second timeout
            var allSendsTask = Task.WhenAll(sendTasks);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var completedTask = await Task.WhenAny(allSendsTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("OTP sends timed out for {User}", sanitizedUserName);
            }
            
            _metrics.IncOtpRequests();
            // Return Accepted to indicate async processing completed (or timed out gracefully)
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
            _logger.LogInformation("OTP Verify: Looking up user {UserName}", Chat.Web.Utilities.LogSanitizer.Sanitize(req.UserName));
            var user = _users.GetByUserName(req.UserName);
            // WORKAROUND: ContentResult to avoid PipeWriter UnflushedBytes issue in test harness.
            if (user == null)
            {
                _logger.LogWarning("OTP Verify: User not found {UserName}", Chat.Web.Utilities.LogSanitizer.Sanitize(req.UserName));
                return new ContentResult { Content = "", ContentType = "text/plain", StatusCode = 401 };
            }
            if (!user.Enabled)
            {
                _logger.LogWarning("OTP Verify: User disabled {UserName}", Chat.Web.Utilities.LogSanitizer.Sanitize(req.UserName));
                return new ContentResult { Content = "", ContentType = "text/plain", StatusCode = 401 };
            }
            
            // Check if user has exceeded maximum verification attempts
            var maxAttempts = _otpOptions.Value?.MaxAttempts ?? 5;
            var attempts = await _otpStore.GetAttemptsAsync(req.UserName);
            if (attempts >= maxAttempts)
            {
                var sanitizedUserName = req.UserName.Replace("\r", "").Replace("\n", "");
                _logger.LogWarning("OTP verification blocked: too many attempts for {User} ({Attempts}/{Max})", 
                    sanitizedUserName, attempts, maxAttempts);
                _metrics.IncOtpVerificationRateLimited();
                // WORKAROUND: Generic response to avoid enumeration + PipeWriter issue
                return new ContentResult { Content = "", ContentType = "text/plain", StatusCode = 401 };
            }
            
            var expected = await _otpStore.GetAsync(req.UserName);
            // WORKAROUND: ContentResult to avoid PipeWriter UnflushedBytes issue in test harness.
            if (string.IsNullOrEmpty(expected)) 
                return new ContentResult { Content = "", ContentType = "text/plain", StatusCode = 401 };
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
            
            if (!ok)
            {
                // Increment failed attempt counter with same TTL as OTP (5 minutes)
                var newAttempts = await _otpStore.IncrementAttemptsAsync(req.UserName, TimeSpan.FromMinutes(5));
                var sanitizedUserName = req.UserName.Replace("\r", "").Replace("\n", "");
                _logger.LogWarning("OTP verification failed for {User} (attempt {Attempts}/{Max})", 
                    sanitizedUserName, newAttempts, maxAttempts);
                // WORKAROUND: ContentResult to avoid PipeWriter UnflushedBytes issue in test harness.
                return new ContentResult { Content = "", ContentType = "text/plain", StatusCode = 401 };
            }

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
            // Let the server decide the safe next URL instead of trusting client input
            var dest = "/chat";
            if (!string.IsNullOrWhiteSpace(req.ReturnUrl) && Url.IsLocalUrl(req.ReturnUrl))
            {
                dest = req.ReturnUrl;
            }
            // WORKAROUND: Manual serialization to avoid PipeWriter UnflushedBytes issue in test harness.
            var payload = new { nextUrl = dest };
            var json = JsonSerializer.Serialize(payload);
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
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
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized();
            }
            
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
        public class VerifyRequest { public string UserName { get; set; } public string Code { get; set; } public string ReturnUrl { get; set; } }

        /// <summary>
        /// Triggers an interactive Microsoft Entra ID sign-in (non-silent) using the default challenge scheme.
        /// Redirects back to a safe local URL (defaults to /chat).
        /// </summary>
        [AllowAnonymous]
        [HttpGet("signin/entra")]
        public IActionResult SignInEntra([FromQuery] string returnUrl = "/chat")
        {
            var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/chat" : (Url.IsLocalUrl(returnUrl) ? returnUrl : "/chat");
            var props = new AuthenticationProperties { RedirectUri = redirect };
            props.Parameters["silent"] = "false";
            return Challenge(props); // uses DefaultChallengeScheme (set to OIDC scheme when Entra is enabled)
        }

        /// <summary>
        /// Lightweight auth debug endpoint (Development use): shows auth state and key Entra settings.
        /// Does not return sensitive data.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("debug")]
        public IActionResult Debug()
        {
            var isAuth = User?.Identity?.IsAuthenticated == true;
            var name = isAuth ? (User?.Identity?.Name ?? "<null>") : "<anon>";
            var claimTypes = isAuth ? System.Linq.Enumerable.Select(User.Claims, c => c.Type) : Array.Empty<string>();
            var entra = _entraOptions?.Value;
            var payload = new
            {
                authenticated = isAuth,
                name,
                claims = claimTypes,
                entraEnabled = entra?.IsEnabled == true,
                tenantId = entra?.TenantId,
                requireTenantValidation = entra?.Authorization?.RequireTenantValidation ?? false,
                allowedTenantsCount = entra?.Authorization?.AllowedTenants?.Count ?? 0
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }
    }
}
