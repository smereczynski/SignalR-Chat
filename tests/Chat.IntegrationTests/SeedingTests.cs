using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Chat.IntegrationTests
{
    public class SeedingTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public SeedingTests(CustomWebApplicationFactory factory) => _factory = factory;

        [Fact]
        public async Task Seeded_Rooms_And_Users_Exist()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", "alice");

            // Rooms (capture error body if request fails for diagnostics)
            var roomsResp = await client.GetAsync("/api/Rooms");
            if (!roomsResp.IsSuccessStatusCode)
            {
                var body = await roomsResp.Content.ReadAsStringAsync();
                throw new Xunit.Sdk.XunitException($"GET /api/Rooms failed: {(int)roomsResp.StatusCode} {roomsResp.ReasonPhrase} Body: {body}");
            }
            var rooms = await roomsResp.Content.ReadFromJsonAsync<RoomStub[]>();
            Assert.NotNull(rooms);
            // Alice should only see general + ops (filtered by FixedRooms)
            Assert.Equal(new[] { "general", "ops" }, rooms!.Select(r => r.Name).OrderBy(n => n));

            // Users (use hub profile echo via connect) - simplified: just hit /api/auth/me after auth stub
            var me = await client.GetFromJsonAsync<UserMeStub>("/api/auth/me");
            Assert.NotNull(me);
            Assert.Equal("alice", me!.UserName);
        }

    private record RoomStub(int Id, string Name);
        private record UserMeStub(string UserName, string FullName, string Avatar);
    }
}