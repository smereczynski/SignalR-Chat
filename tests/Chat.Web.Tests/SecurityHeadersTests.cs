using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;

namespace Chat.Web.Tests
{
    /// <summary>
    /// Tests for SecurityHeadersMiddleware, Content Security Policy, and HSTS implementation.
    /// Verifies that all required security headers are present on responses.
    /// </summary>
    public class SecurityHeadersTests : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;

        public SecurityHeadersTests(WebApplicationFactory<Startup> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Testing:InMemory"] = "true"
                    });
                });
            });
        }

        [Theory]
        [InlineData("/login")]
        [InlineData("/chat")]
        [InlineData("/healthz")]
        [InlineData("/api/localization/strings")]
        public async Task SecurityHeaders_ShouldBePresent_OnAllEndpoints(string endpoint)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Act
            var response = await client.GetAsync(endpoint);

            // Assert - Allow success, redirect, or unauthorized status codes
            Assert.True(response.IsSuccessStatusCode || 
                       response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                       response.StatusCode == System.Net.HttpStatusCode.Found ||
                       response.StatusCode == System.Net.HttpStatusCode.Unauthorized,
                $"Expected success, redirect, or unauthorized for {endpoint}, got {response.StatusCode}");
            
            // Verify all security headers are present
            Assert.True(response.Headers.Contains("Content-Security-Policy"), 
                $"CSP header missing on {endpoint}");
            Assert.True(response.Headers.Contains("X-Content-Type-Options"), 
                $"X-Content-Type-Options header missing on {endpoint}");
            Assert.True(response.Headers.Contains("X-Frame-Options"), 
                $"X-Frame-Options header missing on {endpoint}");
            Assert.True(response.Headers.Contains("Referrer-Policy"), 
                $"Referrer-Policy header missing on {endpoint}");
            
            // Verify header values
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
            Assert.Equal("strict-origin-when-cross-origin", 
                response.Headers.GetValues("Referrer-Policy").First());
        }

        [Fact]
        public async Task CSP_ShouldContainAllRequiredDirectives()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/login");

            // Assert
            var cspHeader = response.Headers.GetValues("Content-Security-Policy").First();
            
            // Verify all required CSP directives
            Assert.Contains("default-src 'self'", cspHeader);
            Assert.Contains("script-src 'self'", cspHeader);
            Assert.Contains("style-src 'self' 'unsafe-inline'", cspHeader);
            Assert.Contains("connect-src 'self' wss: https:", cspHeader); // WebSocket for SignalR
            Assert.Contains("img-src 'self' data: https:", cspHeader);
            Assert.Contains("font-src 'self' data:", cspHeader);
            Assert.Contains("frame-ancestors 'none'", cspHeader); // Clickjacking prevention
            Assert.Contains("base-uri 'self'", cspHeader);
            Assert.Contains("form-action 'self'", cspHeader);
            
            // Verify unsafe-eval is NOT present (security best practice)
            Assert.DoesNotContain("'unsafe-eval'", cspHeader);
        }

        [Fact]
        public async Task CSP_Nonce_ShouldBeUnique_AndUsedInHTML()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act - Make two separate requests
            var response1 = await client.GetAsync("/login");
            var content1 = await response1.Content.ReadAsStringAsync();
            
            var response2 = await client.GetAsync("/login");
            var content2 = await response2.Content.ReadAsStringAsync();

            // Assert
            var csp1 = response1.Headers.GetValues("Content-Security-Policy").First();
            var csp2 = response2.Headers.GetValues("Content-Security-Policy").First();
            
            // Verify nonce is present in CSP header
            Assert.Contains("'nonce-", csp1);
            
            // Extract nonce from CSP header
            var nonce1 = ExtractNonceFromCSP(csp1);
            var nonce2 = ExtractNonceFromCSP(csp2);
            
            // Verify nonce has reasonable length (base64 of 16 bytes = 24 chars)
            Assert.True(nonce1.Length >= 20, $"Nonce should be at least 20 characters, got: {nonce1.Length}");
            
            // Verify nonces are different between requests (security requirement)
            Assert.NotEqual(nonce1, nonce2);
            
            // Verify the same nonce appears in the HTML (in script tag)
            // The nonce attribute in Razor pages uses @HttpContext.Items["csp-nonce"]
            // In the rendered HTML, this could be either:
            // 1. HTML-encoded as nonce=&quot;...&quot; (most common)
            // 2. Unencoded as nonce="..." (less common)
            // 3. Or the script tag might not have a nonce attribute if rendering failed
            var hasNonce1 = content1.Contains($"nonce=&quot;{nonce1}&quot;") || 
                           content1.Contains($"nonce=\"{nonce1}\"") ||
                           content1.Contains($"nonce={nonce1}");
            var hasNonce2 = content2.Contains($"nonce=&quot;{nonce2}&quot;") || 
                           content2.Contains($"nonce=\"{nonce2}\"") ||
                           content2.Contains($"nonce={nonce2}");
            
            Assert.True(hasNonce1,
                $"Nonce '{nonce1}' should appear in HTML response 1. " +
                $"Looking for patterns: nonce=&quot;{nonce1}&quot; or nonce=\"{nonce1}\" or nonce={nonce1}");
            Assert.True(hasNonce2,
                $"Nonce '{nonce2}' should appear in HTML response 2. " +
                $"Looking for patterns: nonce=&quot;{nonce2}&quot; or nonce=\"{nonce2}\" or nonce={nonce2}");
        }

        private string ExtractNonceFromCSP(string cspHeader)
        {
            var noncePattern = "'nonce-";
            var nonceStart = cspHeader.IndexOf(noncePattern);
            if (nonceStart == -1)
                return string.Empty;
            nonceStart += noncePattern.Length;
            var nonceEnd = cspHeader.IndexOf("'", nonceStart);
            if (nonceEnd == -1 || nonceEnd <= nonceStart)
                return string.Empty;
            return cspHeader.Substring(nonceStart, nonceEnd - nonceStart);
        }

        // Note: We only test Development mode here because HSTS is configured to only apply
        // in non-Development environments (Production, Staging, etc.). Testing Production mode
        // would require mocking external dependencies (Cosmos DB, Redis) which is outside the
        // scope of these unit tests. The configuration itself is verified in the code, and
        // HSTS behavior in Production can be verified manually or via integration tests.

        [Fact]
        public async Task HSTS_ShouldNotBePresent_InDevelopmentMode()
        {
            // Arrange - Create factory with Development environment
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Testing:InMemory"] = "true"
                    });
                });
            });
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Act
            var response = await client.GetAsync("/login");

            // Assert
            Assert.False(response.Headers.Contains("Strict-Transport-Security"),
                "HSTS header should NOT be present in Development environment");
        }

        [Fact]
        public async Task HSTS_ShouldNotBePresent_InTestingInMemoryMode()
        {
            // Arrange - Testing:InMemory should behave like Development
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Act
            var response = await client.GetAsync("/login");

            // Assert
            Assert.False(response.Headers.Contains("Strict-Transport-Security"),
                "HSTS header should NOT be present when Testing:InMemory=true (test mode)");
        }
    }
}
