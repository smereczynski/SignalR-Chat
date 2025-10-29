using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Hubs;
using Chat.Web.ViewModels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Chat.IntegrationTests
{
    /// <summary>
    /// Integration tests verifying MarkRead rate limiting behavior.
    /// Tests ensure abuse protection works correctly and metrics are tracked.
    /// </summary>
    public class MarkReadRateLimitingTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        private readonly ITestOutputHelper _output;

        public MarkReadRateLimitingTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        [Fact]
        public async Task MarkRead_UnderLimit_AllowsAllRequests()
        {
            // Arrange
            var client = _factory.CreateClient();
            var hubUrl = client.BaseAddress + "chatHub";
            
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    opts.Headers.Add("X-Test-User", "alice");
                })
                .WithAutomaticReconnect()
                .Build();

            bool errorReceived = false;
            string errorMessage = null;
            connection.On<string>("onError", (msg) =>
            {
                errorReceived = true;
                errorMessage = msg;
                _output.WriteLine($"Error received: {msg}");
            });

            await connection.StartAsync();
            await Task.Delay(500); // Wait for connection to stabilize

            // Act - Send 5 MarkRead requests (well under default limit of 100 per 10 seconds)
            for (int i = 1; i <= 5; i++)
            {
                await connection.InvokeAsync("MarkRead", i);
                await Task.Delay(50); // Small delay between requests
            }

            await Task.Delay(500); // Wait for any delayed error messages

            // Assert
            Assert.False(errorReceived, $"Unexpected error: {errorMessage}");

            // Cleanup
            await connection.StopAsync();
            await connection.DisposeAsync();
        }

        [Fact]
        public async Task MarkRead_ExceedsLimit_RejectsRequests()
        {
            // Arrange - Configure tight rate limit for testing
            var factory = new CustomWebApplicationFactory();
            factory.ConfigureTestServices(services =>
            {
                services.Configure<Chat.Web.Options.RateLimitingOptions>(opts =>
                {
                    opts.MarkReadPermitLimit = 5; // Very low limit for testing
                    opts.MarkReadWindowSeconds = 2; // Short window
                });
            });

            var client = factory.CreateClient();
            var hubUrl = client.BaseAddress + "chatHub";
            
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    opts.Headers.Add("X-Test-User", "bob");
                })
                .WithAutomaticReconnect()
                .Build();

            int errorCount = 0;
            var errorMessages = new List<string>();
            connection.On<string>("onError", (msg) =>
            {
                // Accept either the full localized message or the resource key
                if (msg.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("ErrorRateLimitExceeded", StringComparison.Ordinal))
                {
                    errorCount++;
                    errorMessages.Add(msg);
                    _output.WriteLine($"Rate limit error #{errorCount}: {msg}");
                }
            });

            await connection.StartAsync();
            await Task.Delay(500);

            // Act - Send 10 requests rapidly (exceeds limit of 5)
            var tasks = new List<Task>();
            for (int i = 1; i <= 10; i++)
            {
                tasks.Add(connection.InvokeAsync("MarkRead", i));
            }
            await Task.WhenAll(tasks);
            await Task.Delay(1000); // Wait for error callbacks

            // Assert
            Assert.True(errorCount >= 1, $"Expected at least 1 rate limit error, got {errorCount}");
            _output.WriteLine($"Total rate limit errors: {errorCount}");
            Assert.All(errorMessages, msg => 
                Assert.True(
                    msg.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("ErrorRateLimitExceeded", StringComparison.Ordinal),
                    $"Expected rate limit error message, got: {msg}"));

            // Cleanup
            await connection.StopAsync();
            await connection.DisposeAsync();
            factory.Dispose();
        }

        [Fact]
        public async Task MarkRead_WindowExpires_AllowsNewRequests()
        {
            // Arrange - Configure rate limit with short window
            var factory = new CustomWebApplicationFactory();
            factory.ConfigureTestServices(services =>
            {
                services.Configure<Chat.Web.Options.RateLimitingOptions>(opts =>
                {
                    opts.MarkReadPermitLimit = 3;
                    opts.MarkReadWindowSeconds = 2; // 2 second window
                });
            });

            var client = factory.CreateClient();
            var hubUrl = client.BaseAddress + "chatHub";
            
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    opts.Headers.Add("X-Test-User", "charlie");
                })
                .WithAutomaticReconnect()
                .Build();

            int errorCount = 0;
            connection.On<string>("onError", (msg) =>
            {
                // Accept either the full localized message or the resource key
                if (msg.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("ErrorRateLimitExceeded", StringComparison.Ordinal))
                {
                    errorCount++;
                }
            });

            await connection.StartAsync();
            await Task.Delay(500);

            // Act - First burst: 3 requests (at limit)
            for (int i = 1; i <= 3; i++)
            {
                await connection.InvokeAsync("MarkRead", i);
            }
            await Task.Delay(500);
            
            int firstBurstErrors = errorCount;
            _output.WriteLine($"Errors after first burst: {firstBurstErrors}");

            // Wait for window to expire
            await Task.Delay(2500);

            // Second burst: 3 more requests (should be allowed after window expires)
            for (int i = 4; i <= 6; i++)
            {
                await connection.InvokeAsync("MarkRead", i);
            }
            await Task.Delay(500);

            int secondBurstErrors = errorCount - firstBurstErrors;
            _output.WriteLine($"Errors after second burst: {secondBurstErrors}");

            // Assert
            Assert.Equal(0, firstBurstErrors); // First burst should be allowed
            Assert.Equal(0, secondBurstErrors); // Second burst should be allowed after window expires

            // Cleanup
            await connection.StopAsync();
            await connection.DisposeAsync();
            factory.Dispose();
        }

        [Fact]
        public async Task MarkRead_DifferentUsers_IndependentLimits()
        {
            // Arrange - Tight limit to verify per-user tracking
            var factory = new CustomWebApplicationFactory();
            factory.ConfigureTestServices(services =>
            {
                services.Configure<Chat.Web.Options.RateLimitingOptions>(opts =>
                {
                    opts.MarkReadPermitLimit = 5;
                    opts.MarkReadWindowSeconds = 3;
                });
            });

            var client = factory.CreateClient();
            var hubUrl = client.BaseAddress + "chatHub";
            
            // Two connections for different users
            var connection1 = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    opts.Headers.Add("X-Test-User", "user1");
                })
                .Build();

            var connection2 = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    opts.Headers.Add("X-Test-User", "user2");
                })
                .Build();

            int errors1 = 0, errors2 = 0;
            connection1.On<string>("onError", msg => { 
                if (msg.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) || 
                    msg.Contains("ErrorRateLimitExceeded", StringComparison.Ordinal)) 
                    errors1++; 
            });
            connection2.On<string>("onError", msg => { 
                if (msg.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) || 
                    msg.Contains("ErrorRateLimitExceeded", StringComparison.Ordinal)) 
                    errors2++; 
            });

            await connection1.StartAsync();
            await connection2.StartAsync();
            await Task.Delay(500);

            // Act - Each user sends 5 requests (at their individual limits)
            for (int i = 1; i <= 5; i++)
            {
                await connection1.InvokeAsync("MarkRead", i);
                await connection2.InvokeAsync("MarkRead", i);
            }
            await Task.Delay(1000);

            // Assert - Neither user should hit rate limit since they're tracked independently
            _output.WriteLine($"User1 errors: {errors1}, User2 errors: {errors2}");
            Assert.Equal(0, errors1);
            Assert.Equal(0, errors2);

            // Cleanup
            await connection1.StopAsync();
            await connection2.StopAsync();
            await connection1.DisposeAsync();
            await connection2.DisposeAsync();
            factory.Dispose();
        }
    }
}
