using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

        [Fact]
        public async Task RealMode_Healthz_Ready_Returns200_WithOverriddenChecks()
        {
            var app = _factory.WithWebHostBuilder(builder =>
            {
                // Force real-mode branch while providing placeholders
                builder.UseSetting("Testing:InMemory", "false");
                builder.UseSetting("Cosmos:ConnectionString", "AccountEndpoint=https://localhost:8081/;AccountKey=FAKE==;");
                builder.UseSetting("Redis:ConnectionString", "localhost:6379,abortConnect=false");
                builder.ConfigureServices(services =>
                {
                    // Override health checks to only include a simple healthy check
                    services.PostConfigure<HealthCheckServiceOptions>(opts =>
                    {
                        opts.Registrations.Clear();
                        opts.Registrations.Add(new HealthCheckRegistration(
                            name: "self",
                            instance: new Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck(_ => Task.FromResult(HealthCheckResult.Healthy())),
                            failureStatus: null,
                            tags: new[] { "ready" }));
                    });
                });
            });
            var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var h1 = await client.GetAsync("/healthz");
            var h2 = await client.GetAsync("/healthz/ready");
            Assert.Equal(HttpStatusCode.OK, h1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, h2.StatusCode);
        }
    }
}
