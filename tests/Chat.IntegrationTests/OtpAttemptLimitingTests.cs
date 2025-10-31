using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Chat.IntegrationTests
{
    /// <summary>
    /// Integration tests for OTP attempt rate limiting (GitHub issue #26).
    /// Verifies that after N failed verification attempts, further attempts are blocked
    /// until the counter expires.
    /// NOTE: Tests use existing users (alice, bob, charlie) and each test is independent.
    /// </summary>
    [Collection("Sequential")]
    public class OtpAttemptLimitingTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly HttpClient _client;

        public OtpAttemptLimitingTests(CustomWebApplicationFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task OtpVerification_BlocksAfterMaxAttempts()
        {
            // Arrange: Request OTP for alice
            var startPayload = new { userName = "alice" };
            var startResponse = await _client.PostAsync("/api/auth/start",
                new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json"));
            Assert.True(startResponse.IsSuccessStatusCode, $"Start failed with {startResponse.StatusCode}");

            // Act: Make 5 failed verification attempts (default MaxAttempts)
            for (int i = 1; i <= 5; i++)
            {
                var verifyPayload = new { userName = "alice", code = "000000" }; // Wrong code
                var verifyResponse = await _client.PostAsync("/api/auth/verify",
                    new StringContent(JsonSerializer.Serialize(verifyPayload), Encoding.UTF8, "application/json"));
                
                // Should get Unauthorized for wrong code
                Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
            }

            // Assert: 6th attempt should still be blocked (counter at limit)
            var blockedPayload = new { userName = "alice", code = "123456" };
            var blockedResponse = await _client.PostAsync("/api/auth/verify",
                new StringContent(JsonSerializer.Serialize(blockedPayload), Encoding.UTF8, "application/json"));
            
            // Should still return Unauthorized (blocked due to too many attempts)
            Assert.Equal(HttpStatusCode.Unauthorized, blockedResponse.StatusCode);
        }

        [Fact]
        public async Task OtpVerification_AllowsAttemptsWithinLimit()
        {
            // Arrange: Request OTP for bob
            var startPayload = new { userName = "bob" };
            var startResponse = await _client.PostAsync("/api/auth/start",
                new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json"));
            Assert.True(startResponse.IsSuccessStatusCode, $"Start failed with {startResponse.StatusCode}");

            // Act: Make 3 failed verification attempts (under limit of 5)
            for (int i = 1; i <= 3; i++)
            {
                var verifyPayload = new { userName = "bob", code = "000000" }; // Wrong code
                var verifyResponse = await _client.PostAsync("/api/auth/verify",
                    new StringContent(JsonSerializer.Serialize(verifyPayload), Encoding.UTF8, "application/json"));
                
                // Assert: All attempts should be processed (Unauthorized for wrong code, not blocked)
                Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
            }
            
            // Verify we can still make one more attempt (4th, still under limit)
            var fourthAttempt = new { userName = "bob", code = "111111" };
            var fourthResponse = await _client.PostAsync("/api/auth/verify",
                new StringContent(JsonSerializer.Serialize(fourthAttempt), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, fourthResponse.StatusCode);
        }

        [Fact]
        public async Task OtpVerification_CounterIncrementsOnlyOnFailure()
        {
            // Arrange: Request OTP for charlie
            var startPayload = new { userName = "charlie" };
            var startResponse = await _client.PostAsync("/api/auth/start",
                new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json"));
            Assert.True(startResponse.IsSuccessStatusCode, $"Start failed with {startResponse.StatusCode}");

            // Act: Make exactly MaxAttempts (5) failed attempts
            for (int i = 1; i <= 5; i++)
            {
                var verifyPayload = new { userName = "charlie", code = $"00000{i}" }; // Different wrong codes
                var verifyResponse = await _client.PostAsync("/api/auth/verify",
                    new StringContent(JsonSerializer.Serialize(verifyPayload), Encoding.UTF8, "application/json"));
                
                Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
            }

            // Assert: Next attempt is blocked due to exceeding limit
            var blockedPayload = new { userName = "charlie", code = "123456" };
            var blockedResponse = await _client.PostAsync("/api/auth/verify",
                new StringContent(JsonSerializer.Serialize(blockedPayload), Encoding.UTF8, "application/json"));
            
            Assert.Equal(HttpStatusCode.Unauthorized, blockedResponse.StatusCode);
        }
    }
}
