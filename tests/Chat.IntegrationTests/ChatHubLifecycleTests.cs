using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Chat.IntegrationTests
{
    public class ChatHubLifecycleTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public ChatHubLifecycleTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private HubConnection CreateConnection(string user)
        {
            var handler = _factory.Server.CreateHandler();
            var baseAddress = _factory.Server.BaseAddress?.ToString() ?? "http://localhost";
            var connection = new HubConnectionBuilder()
                .WithUrl(baseAddress + "chatHub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => handler;
                    options.Headers.Add("X-Test-User", user);
                })
                .WithAutomaticReconnect()
                .Build();
            return connection;
        }

        [Fact]
        public async Task DuplicateConnections_DoNotThrow()
        {
            var user = Guid.NewGuid().ToString("n").Substring(0,8);
            var c1 = CreateConnection(user);
            await c1.StartAsync();
            var c2 = CreateConnection(user);
            await c2.StartAsync();
            // If we reach here without exception, duplicate mapping handled.
            await c1.DisposeAsync();
            await c2.DisposeAsync();
        }

        [Fact]
        public async Task DisconnectWithoutRoom_DoesNotThrow()
        {
            var user = Guid.NewGuid().ToString("n").Substring(0,8);
            var c1 = CreateConnection(user);
            await c1.StartAsync();
            await c1.DisposeAsync();
        }

        [Fact]
        public async Task JoinLeaveCycle_Works()
        {
            var user = Guid.NewGuid().ToString("n").Substring(0,8);
            var c = CreateConnection(user);
            await c.StartAsync();
            await c.InvokeAsync("Join", "general");
            await c.InvokeAsync("Leave", "general");
            await c.DisposeAsync();
        }

        [Fact]
        public async Task SwitchingRooms_UpdatesState()
        {
            var user = Guid.NewGuid().ToString("n").Substring(0,8);
            var c = CreateConnection(user);
            await c.StartAsync();
            await c.InvokeAsync("Join", "general");
            await c.InvokeAsync("Join", "random"); // joining new room triggers leave of previous
            await c.DisposeAsync();
        }

        [Fact]
        public async Task DoubleDisconnect_IsIdempotent()
        {
            var user = Guid.NewGuid().ToString("n").Substring(0,8);
            var c = CreateConnection(user);
            await c.StartAsync();
            await c.DisposeAsync();
            // Second dispose should be safe / no throw
            await c.DisposeAsync();
        }
    }
}
