using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUsersRepository _users;
        private readonly IOtpStore _otpStore;
        private readonly IOtpSender _otpSender;

        public AuthController(IUsersRepository users, IOtpStore otpStore, IOtpSender otpSender)
        {
            _users = users;
            _otpStore = otpStore;
            _otpSender = otpSender;
        }

    [AllowAnonymous]
    [HttpPost("start")]
        public async Task<IActionResult> Start([FromBody] StartRequest req)
        {
            var user = _users.GetByUserName(req.UserName);
            if (user == null) return Unauthorized();

            var code = new Random().Next(100000, 999999).ToString();
            await _otpStore.SetAsync(req.UserName, code, TimeSpan.FromMinutes(5));
            await _otpSender.SendAsync(req.UserName, req.Destination ?? req.UserName, code);
            return Ok();
        }

    [AllowAnonymous]
    [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] VerifyRequest req)
        {
            var expected = await _otpStore.GetAsync(req.UserName);
            if (string.IsNullOrEmpty(expected) || expected != req.Code)
                return Unauthorized();

            await _otpStore.RemoveAsync(req.UserName);

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

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
        }

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

        public class StartRequest { public string UserName { get; set; } public string Destination { get; set; } }
        public class VerifyRequest { public string UserName { get; set; } public string Code { get; set; } }
    }
}
