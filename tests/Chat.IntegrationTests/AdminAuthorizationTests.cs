using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.IntegrationTests
{
    /// <summary>
    /// Integration tests for admin panel authorization with home tenant restriction.
    /// Tests verify that only home tenant users with Admin.ReadWrite role can access admin pages.
    /// External tenant users are denied access even if they have the admin role in their own tenant.
    /// </summary>
    public class AdminAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        private const string HomeTenantId = "aaaaaaaa-1111-1111-1111-111111111111";
        private const string ExternalTenantId = "bbbbbbbb-2222-2222-2222-222222222222";

        public AdminAuthorizationTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private HttpClient CreateAuthenticatedClient(List<Claim> claims)
        {
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationScheme, 
                        options => { });
                });
            }).CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Add claims to request headers
            var claimsData = claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();
            var claimsJson = System.Text.Json.JsonSerializer.Serialize(claimsData);
            client.DefaultRequestHeaders.Add("X-Test-Claims", claimsJson);

            return client;
        }

        [Fact]
        public async Task AdminIndex_WithHomeTenantAdmin_ReturnsSuccess()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin@contoso.com"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim("tid", HomeTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AdminIndex_WithExternalTenantAdmin_ReturnsForbidden()
        {
            // Arrange
            // External tenant user with Admin.ReadWrite role (should be denied)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin@fabrikam.com"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim("tid", ExternalTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AdminIndex_WithNoAuthentication_ReturnsUnauthorized()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Act
            var response = await client.GetAsync("/Admin");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AdminIndex_WithAuthenticatedNonAdmin_ReturnsForbidden()
        {
            // Arrange
            // Home tenant user without Admin.ReadWrite role
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@contoso.com"),
                new Claim(ClaimTypes.Role, "User.Read"),
                new Claim("tid", HomeTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AdminUsersIndex_WithHomeTenantAdmin_ReturnsSuccess()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin@contoso.com"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim("tid", HomeTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin/Users");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AdminRoomsIndex_WithHomeTenantAdmin_ReturnsSuccess()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin@contoso.com"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim("tid", HomeTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin/Rooms");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AdminUsersCreate_WithExternalTenantAdmin_ReturnsForbidden()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin@fabrikam.com"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim("tid", ExternalTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin/Users/Create");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AdminRoomsCreate_WithExternalTenantAdmin_ReturnsForbidden()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "admin@fabrikam.com"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim("tid", ExternalTenantId)
            };
            var client = CreateAuthenticatedClient(claims);

            // Act
            var response = await client.GetAsync("/Admin/Rooms/Create");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>
    /// Test authentication handler for integration tests.
    /// Creates authenticated users with claims from X-Test-Claims header.
    /// </summary>
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string AuthenticationScheme = "TestScheme";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Claims"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claimsJson = Request.Headers["X-Test-Claims"].ToString();
            var claimsData = System.Text.Json.JsonSerializer.Deserialize<List<ClaimData>>(claimsJson);
            
            var claims = new List<Claim>();
            if (claimsData != null)
            {
                foreach (var claimData in claimsData)
                {
                    claims.Add(new Claim(claimData.Type, claimData.Value));
                }
            }

            var identity = new ClaimsIdentity(claims, AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        private class ClaimData
        {
            public string Type { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }
    }
}
