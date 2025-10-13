using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;

public class BasicTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BasicTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Healthz_Returns_Ok()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/healthz");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var txt = await res.Content.ReadAsStringAsync();
        txt.Should().Be("ok");
    }

    [Fact]
    public async Task Unauthenticated_Users_Index_Redirects_To_Login()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/Users");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location.Should().NotBeNull();
        // We don't assert exact AAD URL since it's env-dependent, but it should be a redirect
    }

    [Fact]
    public async Task Authenticated_Rooms_Index_Renders()
    {
        var authedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace Cosmos-backed repos with in-memory fakes for tests
                services.RemoveAll(typeof(IRoomsRepository));
                services.AddSingleton<IRoomsRepository, InMemoryRoomsRepository>();

                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                services.PostConfigure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(options =>
                {
                    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Test").RequireAuthenticatedUser().Build();
                    options.FallbackPolicy = options.DefaultPolicy;
                });
            });
        });

        var client = authedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/Rooms");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("Rooms");
    }
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "test-user") };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, Scheme.Name);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// Simple in-memory implementation used for tests
public class InMemoryRoomsRepository : IRoomsRepository
{
    private readonly List<string> _rooms = new() { "general", "random" };
    public Task<IEnumerable<string>> GetAllNamesAsync()
        => Task.FromResult<IEnumerable<string>>(_rooms.ToList());

    public Task CreateAsync(string name)
    {
        if (!_rooms.Contains(name)) _rooms.Add(name);
        return Task.CompletedTask;
    }
}
