using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using Chat.Web.Models;
using System.Linq;

namespace Chat.IntegrationTests
{
    public class RoomsAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public RoomsAuthorizationTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Theory]
        [InlineData("alice", new[] { "general", "ops" })]
        [InlineData("bob", new[] { "general", "random" })]
        [InlineData("charlie", new[] { "general" })]
        public async Task ReturnsOnlyAuthorizedRooms(string user, string[] expected)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", user);
            var resp = await client.GetAsync("/api/Rooms");
            resp.EnsureSuccessStatusCode();
            var rooms = await resp.Content.ReadFromJsonAsync<RoomDto[]>() ?? new RoomDto[0];
            var names = rooms.Select(r => r.Name).OrderBy(n => n).ToArray();
            Assert.Equal(expected.OrderBy(n => n), names);
        }

        [Fact]
        public async Task AnonymousCannotListRooms()
        {
            var client = _factory.CreateClient();
            var resp = await client.GetAsync("/api/Rooms");
            // In test harness we use custom header auth; missing header means anonymous and the [Authorize] attribute should yield 401.
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        private record RoomDto(int Id, string Name, string Admin);
    }
}
