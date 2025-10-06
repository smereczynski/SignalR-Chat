using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Chat.IntegrationTests
{
    /// <summary>
    /// Regression test ensuring that an immediate message POST right after authentication succeeds
    /// and the message is retrievable in subsequent room history fetch.
    /// This simulates a user pressing Enter extremely quickly after login (prevents the prior race where
    /// the first message was effectively lost until refresh/client-side queue flush).
    /// </summary>
    public class ImmediatePostAfterLoginTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        public ImmediatePostAfterLoginTests(CustomWebApplicationFactory factory) => _factory = factory;

        private record CreateMessageDto(string Room, string Content, string CorrelationId);
        private record MessageVm(int Id, string Content, string FromUserName, string FromFullName, string Avatar, string Room, DateTime Timestamp, string CorrelationId);

        [Fact]
        public async Task FirstImmediatePost_AppearsInRecentRoomMessages()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", "alice"); // alice has access to general

            // Step 0: Simulate auth probe that the browser performs (/api/auth/me)
            var meResp = await client.GetAsync("/api/auth/me");
            meResp.EnsureSuccessStatusCode();

            // Step 1: Immediately send message after probe
            var correlationId = "test_" + Guid.NewGuid().ToString("N").Substring(0,8);
            var createResponse = await client.PostAsJsonAsync("/api/Messages", new CreateMessageDto("general", "hello-immediate", correlationId));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            // Parse created message
            var created = await createResponse.Content.ReadFromJsonAsync<MessageVm>();
            Assert.NotNull(created);
            Assert.Equal("alice", created!.FromUserName);
            Assert.Equal("general", created.Room);
            Assert.Equal("hello-immediate", created.Content);
            Assert.Equal(correlationId, created.CorrelationId);

            // Step 2: Fetch recent messages for room and ensure presence
            var list = await client.GetFromJsonAsync<MessageVm[]>("/api/Messages/Room/general?take=50");
            Assert.NotNull(list);
            Assert.Contains(list!, m => m.Id == created.Id || (m.CorrelationId == correlationId && m.Content == "hello-immediate"));
        }
    }
}
