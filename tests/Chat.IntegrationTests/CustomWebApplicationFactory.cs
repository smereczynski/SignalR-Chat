using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Chat.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Chat.Web.Repositories; // reference for service resolution only

namespace Chat.IntegrationTests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                var dict = new Dictionary<string, string>
                {
                    ["Testing:InMemory"] = "true", // instruct Startup to use in-memory stores
                    ["Cosmos:ConnectionString"] = "placeholder", // will not be used due to in-memory flag
                    ["Redis:ConnectionString"] = "placeholder",
                    // Test-tuned rate limiter (fast window for deterministic rejection & quick reset)
                    ["RateLimiting:Auth:PermitLimit"] = "5",
                    ["RateLimiting:Auth:WindowSeconds"] = "5",
                    ["RateLimiting:Auth:QueueLimit"] = "0"
                };
                config.AddInMemoryCollection(dict!);
            });
        }
    }
}
