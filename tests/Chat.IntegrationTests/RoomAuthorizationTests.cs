using System.Net;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;

namespace Chat.IntegrationTests
{
    public class RoomAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public RoomAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

        [Fact]
        public async Task User_Cannot_Join_Unassigned_Room()
        {
            // Test fixture provides charlie with access only to "general"; attempt to join "ops" should trigger onError
            var baseClient = _factory.CreateClient();
            baseClient.DefaultRequestHeaders.Add("X-Test-User", "charlie");
            var hubUrl = baseClient.BaseAddress + "chatHub";

            var errors = new ConcurrentQueue<string>();
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, o => { o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler(); o.Headers.Add("X-Test-User", "charlie"); })
                .WithAutomaticReconnect()
                .Build();
            connection.On<string>("onError", err => errors.Enqueue(err));

            await connection.StartAsync();
            await connection.InvokeAsync("Join", "ops");

            // small delay to allow server callback
            await Task.Delay(100);
            // Accept either the full localized message or the resource key (in test environment, 
            // resources may not be fully loaded, so IStringLocalizer.Value returns the key)
            Assert.Contains(errors, e => 
                e.Contains("not authorized", System.StringComparison.OrdinalIgnoreCase) ||
                e.Contains("ErrorNotAuthorizedRoom", System.StringComparison.Ordinal));
            await connection.DisposeAsync();
        }
    }
}
