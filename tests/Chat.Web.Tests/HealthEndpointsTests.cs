using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chat.Web.Tests
{
    public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Chat.Web.Startup>>
    {
        private readonly WebApplicationFactory<Chat.Web.Startup> _factory;
        public HealthEndpointsTests(WebApplicationFactory<Chat.Web.Startup> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task InMemoryMode_Healthz_Ready_Returns200()
        {
            var app = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Testing:InMemory", "true");
            });
            var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var h1 = await client.GetAsync("/healthz");
            var h2 = await client.GetAsync("/healthz/ready");
            Assert.Equal(HttpStatusCode.OK, h1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, h2.StatusCode);
        }
    }
}
