using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Chat.IntegrationTests
{
    [Collection("Sequential")]
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
            // Rate limit: 20 permits per 5 seconds (from CustomWebApplicationFactory)
            // Send 40 requests in rapid succession to guarantee exceeding the limit
            const int totalRequests = 40;
            
            // Create all tasks simultaneously using Parallel.For for maximum concurrency
            var taskArray = new Task<HttpResponseMessage>[totalRequests];
            Parallel.For(0, totalRequests, i =>
            {
                taskArray[i] = client.PostAsJsonAsync("/api/auth/start", new StartReq("alice", "alice"));
            });
            
            // Wait for all to complete
            await Task.WhenAll(taskArray);
            
            int tooMany = 0, oks = 0, accepted = 0, other = 0;
            foreach (var task in taskArray)
            {
                var resp = await task;
                if (resp.StatusCode == HttpStatusCode.TooManyRequests) tooMany++;
                else if (resp.StatusCode == HttpStatusCode.OK) oks++;
                else if (resp.StatusCode == HttpStatusCode.Accepted) accepted++;
                else other++;
            }
            
            _output.WriteLine($"Burst summary: Total={totalRequests}, OK={oks}, Accepted={accepted}, 429={tooMany}, Other={other}");
            
            // With 40 requests and limit of 20, we should get at least a few 429s
            // Allow for some timing variance in CI environments
            Assert.True(tooMany >= 1, $"Expected at least one 429 but got OK={oks}, 429={tooMany}, Other={other}");
        }
    }
}
