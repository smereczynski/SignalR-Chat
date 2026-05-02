using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Chat.Web.Controllers;
using Chat.Web.Options;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Chat.Tests;

public class AuthControllerTests
{
    [Fact]
    public async Task Start_UnknownUser_ReturnsUnauthorized()
    {
        var controller = BuildController();

        var result = await controller.Start(new AuthController.StartRequest { UserName = "ghost" });

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Start_SenderFailureStillReturnsAccepted_AndStoresCode()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new Chat.Web.Models.ApplicationUser
        {
            UserName = "alice",
            Email = "alice@example.com",
            Enabled = true
        });
        var otpStore = new InMemoryOtpStore();
        var sender = new ThrowingOtpSender();
        var controller = BuildController(users: users, otpStore: otpStore, otpSender: sender);

        var result = await controller.Start(new AuthController.StartRequest { UserName = "alice" });

        Assert.IsType<AcceptedResult>(result);
        Assert.False(string.IsNullOrWhiteSpace(await otpStore.GetAsync("alice")));
        Assert.Equal(1, sender.CallCount);
    }

    [Fact]
    public async Task Verify_TooManyAttempts_Returns401WithoutSignIn()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new Chat.Web.Models.ApplicationUser { UserName = "alice", Enabled = true });
        var otpStore = new InMemoryOtpStore();
        await otpStore.SetAsync("alice", "123456", TimeSpan.FromMinutes(5));
        await otpStore.IncrementAttemptsAsync("alice", TimeSpan.FromMinutes(5));
        await otpStore.IncrementAttemptsAsync("alice", TimeSpan.FromMinutes(5));

        var authService = new Mock<IAuthenticationService>();
        var controller = BuildController(
            users: users,
            otpStore: otpStore,
            authService: authService.Object,
            otpOptions: new OtpOptions { HashingEnabled = false, MaxAttempts = 2 });

        var result = await controller.Verify(new AuthController.VerifyRequest { UserName = "alice", Code = "123456" });

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(401, content.StatusCode);
        authService.Verify(x => x.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Never);
    }

    [Fact]
    public async Task Verify_ValidCode_SignsIn_RemovesOtp_AndUsesSafeLocalReturnUrl()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new Chat.Web.Models.ApplicationUser { UserName = "alice", Enabled = true });
        var otpStore = new InMemoryOtpStore();
        await otpStore.SetAsync("alice", "123456", TimeSpan.FromMinutes(5));

        var authService = new Mock<IAuthenticationService>();
        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.IsLocalUrl("/safe")).Returns(true);
        urlHelper.Setup(x => x.IsLocalUrl(It.Is<string>(s => s != "/safe"))).Returns(false);

        ClaimsPrincipal? signedInPrincipal = null;
        authService
            .Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, principal, _) => signedInPrincipal = principal)
            .Returns(Task.CompletedTask);

        var controller = BuildController(
            users: users,
            otpStore: otpStore,
            authService: authService.Object,
            urlHelper: urlHelper.Object,
            otpOptions: new OtpOptions { HashingEnabled = false });

        var result = await controller.Verify(new AuthController.VerifyRequest { UserName = "alice", Code = "123456", ReturnUrl = "/safe" });

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, content.StatusCode);
        Assert.Contains("/safe", content.Content);
        Assert.NotNull(signedInPrincipal);
        Assert.Equal("alice", signedInPrincipal!.Identity?.Name);
        Assert.Null(await otpStore.GetAsync("alice"));
    }

    [Fact]
    public async Task Verify_NonLocalReturnUrl_FallsBackToChat()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new Chat.Web.Models.ApplicationUser { UserName = "alice", Enabled = true });
        var otpStore = new InMemoryOtpStore();
        await otpStore.SetAsync("alice", "123456", TimeSpan.FromMinutes(5));

        var authService = new Mock<IAuthenticationService>();
        authService
            .Setup(x => x.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.IsLocalUrl("https://evil.example")).Returns(false);

        var controller = BuildController(
            users: users,
            otpStore: otpStore,
            authService: authService.Object,
            urlHelper: urlHelper.Object,
            otpOptions: new OtpOptions { HashingEnabled = false });

        var result = await controller.Verify(new AuthController.VerifyRequest { UserName = "alice", Code = "123456", ReturnUrl = "https://evil.example" });

        var content = Assert.IsType<ContentResult>(result);
        using var doc = JsonDocument.Parse(content.Content!);
        Assert.Equal("/chat", doc.RootElement.GetProperty("nextUrl").GetString());
    }

    [Fact]
    public async Task Me_WithoutIdentity_ReturnsUnauthorized()
    {
        var controller = BuildController(identityName: null);

        var result = await controller.Me();

        Assert.IsType<UnauthorizedResult>(result);
    }

    private static AuthController BuildController(
        InMemoryUsersRepository? users = null,
        IOtpStore? otpStore = null,
        IOtpSender? otpSender = null,
        IAuthenticationService? authService = null,
        IUrlHelper? urlHelper = null,
        OtpOptions? otpOptions = null,
        string? identityName = null)
    {
        users ??= new InMemoryUsersRepository();
        otpStore ??= new InMemoryOtpStore();
        otpSender ??= new NoOpOtpSender();
        authService ??= Mock.Of<IAuthenticationService>();
        otpOptions ??= new OtpOptions { HashingEnabled = false, MaxAttempts = 5 };

        var metrics = new FakeMetrics();
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(x => x.Hash(It.IsAny<string>(), It.IsAny<string>())).Returns("ignored");
        hasher.Setup(x => x.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(new VerificationResult(true, false));

        var services = new ServiceCollection();
        services.AddSingleton(authService);

        var controller = new AuthController(
            users,
            otpStore,
            otpSender,
            metrics,
            hasher.Object,
            Options.Create(otpOptions),
            NullLogger<AuthController>.Instance,
            Options.Create(new EntraIdOptions()));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
                User = identityName == null
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, identityName)], "TestAuth"))
            }
        };
        controller.Url = urlHelper ?? Mock.Of<IUrlHelper>();
        return controller;
    }

    private sealed class NoOpOtpSender : IOtpSender
    {
        public Task SendAsync(string userName, string destination, string code) => Task.CompletedTask;
    }

    private sealed class ThrowingOtpSender : IOtpSender
    {
        public int CallCount { get; private set; }
        public Task SendAsync(string userName, string destination, string code)
        {
            CallCount++;
            throw new InvalidOperationException("send failed");
        }
    }

    private sealed class FakeMetrics : IInProcessMetrics
    {
        public DateTimeOffset StartTime => DateTimeOffset.UtcNow;
        public void IncMessagesSent() { }
        public void IncRoomsJoined() { }
        public void IncOtpRequests() { }
        public void IncOtpVerifications() { }
        public void IncOtpVerificationRateLimited() { }
        public void IncReconnectAttempt(int attempt, int delayMs) { }
        public void IncActiveConnections() { }
        public void DecActiveConnections() { }
        public void IncRoomPresence(string roomName) { }
        public void DecRoomPresence(string roomName) { }
        public void UserAvailable(string userName) { }
        public void UserUnavailable(string userName) { }
        public void IncMarkReadRateLimitViolation(string userName) { }
        public MetricsSnapshot Snapshot() => new(0, 0, 0, 0, 0, 0, TimeSpan.Zero);
    }
}