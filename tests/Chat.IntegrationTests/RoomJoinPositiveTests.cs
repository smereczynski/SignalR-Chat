using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests
{
    public class RoomJoinPositiveTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public RoomJoinPositiveTests(CustomWebApplicationFactory factory) => _factory = factory;

        [Fact]
        public async Task Alice_Can_Join_All_Assigned_Rooms()
        {
            // alice seeded for general, ops
            var baseClient = _factory.CreateClient();
            baseClient.DefaultRequestHeaders.Add("X-Test-User", "alice");
            var hubUrl = baseClient.BaseAddress + "chatHub";

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, o => {
                    o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    o.Headers.Add("X-Test-User", "alice");
                })
                .WithAutomaticReconnect()
                .Build();

            await connection.StartAsync();

            // Join general then ops
            await connection.InvokeAsync("Join", "general");
            await Task.Delay(50);
            await connection.InvokeAsync("Join", "ops");
            await Task.Delay(50);

            // Fetch users for ops and assert alice present
            var users = await connection.InvokeAsync<UserViewModelStub[]>("GetUsers", "ops");
            Assert.Contains(users, u => u.UserName == "alice");

            await connection.DisposeAsync();
        }

        private record UserViewModelStub(string UserName, string FullName, string Avatar, string Device, string CurrentRoom);
    }
}