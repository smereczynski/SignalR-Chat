using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Chat.IntegrationTests
{
    public class RateLimitingTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        private readonly Xunit.Abstractions.ITestOutputHelper _output;
        public RateLimitingTests(CustomWebApplicationFactory factory, Xunit.Abstractions.ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        private record StartReq(string UserName, string Destination);

        [Fact]
        public async Task BurstRequests_Produce429()
        {
            var client = _factory.CreateClient();
            var tasks = new Task<HttpResponseMessage>[7];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = client.PostAsJsonAsync("/api/auth/start", new StartReq("alice", "alice"));
            }
            await Task.WhenAll(tasks);
            int tooMany = 0, oks = 0;
            foreach (var t in tasks)
            {
                var resp = await t;
                if (resp.StatusCode == HttpStatusCode.TooManyRequests) tooMany++;
                if (resp.StatusCode == HttpStatusCode.OK) oks++;
            }
            _output.WriteLine($"Burst summary: OK={oks}, 429={tooMany}");
            Assert.True(tooMany >= 1, $"Expected at least one 429 but got OK={oks},429={tooMany}");
        }
    }
}
