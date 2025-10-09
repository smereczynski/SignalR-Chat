using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Chat.Web.Services;
using Xunit;

namespace Chat.IntegrationTests
{
    public class OtpAuthFlowTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public OtpAuthFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

        private record StartReq(string UserName);
        [Fact]
        public async Task Start_HashedStorage_WritesVersionedHash()
        {
            var client = _factory.CreateClient();
            var start = await client.PostAsJsonAsync("/api/auth/start", new StartReq("alice"));
            Assert.True(start.StatusCode == HttpStatusCode.OK || start.StatusCode == HttpStatusCode.Accepted);

            // Resolve IOtpStore from server DI and assert stored format is hashed
            using var scope = _factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOtpStore>();
            var stored = await store.GetAsync("alice");
            Assert.False(string.IsNullOrEmpty(stored));
            Assert.StartsWith("OtpHash:v2:argon2id:", stored);
        }

        [Fact]
        public async Task LegacyPlaintext_Mode_StoresPlaintext()
        {
            // Force plaintext mode by overriding config
            var factory = _factory.WithWebHostBuilder(b =>
            {
                b.UseSetting("Otp:HashingEnabled", "false");
            });
            var client = factory.CreateClient();
            var start = await client.PostAsJsonAsync("/api/auth/start", new StartReq("bob"));
            Assert.True(start.StatusCode == HttpStatusCode.OK || start.StatusCode == HttpStatusCode.Accepted);

            using var scope = factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOtpStore>();
            var stored = await store.GetAsync("bob");
            Assert.False(string.IsNullOrEmpty(stored));
            Assert.DoesNotContain("OtpHash:", stored);
        }
    }
}
