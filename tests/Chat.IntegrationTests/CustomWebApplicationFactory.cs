using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Chat.Web.Repositories; // reference for service resolution only

namespace Chat.IntegrationTests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private Action<IServiceCollection> _configureTestServices;
        private bool _initialized = false;
        private readonly object _initLock = new object();

        public void ConfigureTestServices(Action<IServiceCollection> configureServices)
        {
            _configureTestServices = configureServices;
        }

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
                    ["RateLimiting:Auth:QueueLimit"] = "0",
                    ["Features:EnableRestPostMessages"] = "true",
                    // Default MarkRead rate limiting for tests
                    ["RateLimiting:MarkRead:MarkReadPermitLimit"] = "100",
                    ["RateLimiting:MarkRead:MarkReadWindowSeconds"] = "10"
                };
                config.AddInMemoryCollection(dict!);
            });

            if (_configureTestServices != null)
            {
                builder.ConfigureServices(_configureTestServices);
            }
        }

        /// <summary>
        /// Ensures the server has fully started and data has been seeded.
        /// Since hosted services don't run reliably in WebApplicationFactory, we seed directly.
        /// </summary>
        private void EnsureServerStarted()
        {
            if (_initialized) return;
            
            lock (_initLock)
            {
                if (_initialized) return;
                
                // Access the Server property to force lazy initialization
                _ = Server;
                
                // Seed test data directly (DataSeedHostedService doesn't run in test harness)
                var usersRepo = Services.GetRequiredService<IUsersRepository>();
                
                // Only seed if not already seeded
                if (!usersRepo.GetAll().Any())
                {
                    usersRepo.Upsert(new Chat.Web.Models.ApplicationUser
                    {
                        UserName = "alice",
                        FullName = "Alice Johnson",
                        Email = "michal.s@free-media.eu",
                        MobileNumber = "+48604970937",
                        FixedRooms = new List<string> { "general", "ops" },
                        DefaultRoom = "general",
                        Enabled = true
                    });
                    usersRepo.Upsert(new Chat.Web.Models.ApplicationUser
                    {
                        UserName = "bob",
                        FullName = "Bob Stone",
                        Email = "michal.s@free-media.eu",
                        MobileNumber = "+48604970937",
                        FixedRooms = new List<string> { "general", "random" },
                        DefaultRoom = "general",
                        Enabled = true
                    });
                    usersRepo.Upsert(new Chat.Web.Models.ApplicationUser
                    {
                        UserName = "charlie",
                        FullName = "Charlie Fields",
                        Email = "michal.s@free-media.eu",
                        MobileNumber = "+48604970937",
                        FixedRooms = new List<string> { "general" },
                        DefaultRoom = "general",
                        Enabled = true
                    });
                }
                
                // Verify seeding completed
                var finalUsers = usersRepo.GetAll().ToList();
                if (finalUsers.Count < 3)
                {
                    var userNames = string.Join(", ", finalUsers.Select(u => u.UserName));
                    throw new InvalidOperationException(
                        $"Data seeding failed. Expected at least 3 users, found {finalUsers.Count}: [{userNames}]");
                }
                
                _initialized = true;
            }
        }

        /// <summary>
        /// Creates a client after ensuring the server and data seeding are ready.
        /// </summary>
        public new HttpClient CreateClient()
        {
            EnsureServerStarted();
            return base.CreateClient();
        }
    }
}
