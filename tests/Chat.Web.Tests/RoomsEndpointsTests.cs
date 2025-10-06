using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chat.Web.Tests
{
    public class RoomsEndpointsTests : IClassFixture<WebApplicationFactory<Chat.Web.Startup>>
    {
        private readonly WebApplicationFactory<Chat.Web.Startup> _factory;
        public RoomsEndpointsTests(WebApplicationFactory<Chat.Web.Startup> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Testing:InMemory", "true");
            });
        }

        [Fact]
        public async Task Post_Rooms_Returns410()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            // Auth not required to assert 410 because controller short-circuits before model binding logic for creation.
            var resp = await client.PostAsJsonAsync("/api/Rooms", new { name = "temp" });
            Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
        }

        [Fact]
        public async Task Delete_Rooms_Returns410()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var resp = await client.DeleteAsync("/api/Rooms/123");
            Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
        }
    }
}
