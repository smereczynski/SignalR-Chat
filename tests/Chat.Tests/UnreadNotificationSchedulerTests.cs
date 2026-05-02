using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Options;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Tests
{
    public class UnreadNotificationSchedulerTests
    {
        private class FakeOtpSender : IOtpSender
        {
            public readonly ConcurrentBag<(string UserNames, string Dest, string Body)> Calls = new();
            public Task SendAsync(string userName, string destination, string code)
            {
                Calls.Add((userName, destination, code));
                return Task.CompletedTask;
            }
        }

        private static (InMemoryRoomsRepository Rooms, InMemoryUsersRepository Users, InMemoryMessagesRepository Messages) SetupRepos()
        {
            var rooms = new InMemoryRoomsRepository();
            var users = new InMemoryUsersRepository(rooms);
            var messages = new InMemoryMessagesRepository();
            return (rooms, users, messages);
        }

        private static ApplicationUser MkUser(string name)
        {
            return new ApplicationUser
            {
                UserName = name,
                FullName = name,
                Email = $"{name}@test.com",
                MobileNumber = $"+1234567{name.GetHashCode() % 10000:0000}",
                Enabled = true
            };
        }

        [Fact]
        public async Task OnTimerAsync_SendsNotificationsToRoomMembersExceptSender()
        {
            var (rooms, users, messages) = SetupRepos();
            await users.UpsertAsync(MkUser("alice"));
            await users.UpsertAsync(MkUser("bob"));
            await users.UpsertAsync(MkUser("charlie"));

            var room = new Room
            {
                Name = "pair:dc-a::dc-b",
                DisplayName = "A <-> B",
                PairKey = "dc-a::dc-b",
                DispatchCenterAId = "dc-a",
                DispatchCenterBId = "dc-b",
                IsActive = true,
                Users = new List<string> { "alice", "bob" }
            };
            await rooms.UpsertAsync(room);

            // Create message from alice in the pair room
            var msg = await messages.CreateAsync(new Message
            {
                Content = "Hello",
                FromUser = await users.GetByUserNameAsync("alice"),
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            });

            var fakeOtp = new FakeOtpSender();
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fakeOtp, opts, NullLogger<UnreadNotificationScheduler>.Instance);
            try
            {
                await InvokeOnTimerAsync(scheduler, msg.Id);

                var otpCalls = fakeOtp.Calls.ToList();
                Assert.Equal(2, otpCalls.Count);
                Assert.All(otpCalls, call => Assert.Equal("bob", call.UserNames));
                Assert.All(otpCalls, call => Assert.Contains("New message in #pair:dc-a::dc-b", call.Body));
                Assert.DoesNotContain(otpCalls, call => call.UserNames.Contains("alice", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
            }
        }

        [Fact]
        public async Task OnTimerAsync_SkipsNotificationsWhenMessageAlreadyRead()
        {
            var (rooms, users, messages) = SetupRepos();
            await users.UpsertAsync(MkUser("alice"));
            await users.UpsertAsync(MkUser("bob"));

            var room = new Room
            {
                Name = "pair:dc-a::dc-b",
                DisplayName = "A <-> B",
                PairKey = "dc-a::dc-b",
                DispatchCenterAId = "dc-a",
                DispatchCenterBId = "dc-b",
                IsActive = true,
                Users = new List<string> { "alice", "bob" }
            };
            await rooms.UpsertAsync(room);
            var msg = await messages.CreateAsync(new Message
            {
                Content = "Check read",
                FromUser = await users.GetByUserNameAsync("alice"),
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            });

            var fakeOtp = new FakeOtpSender();
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fakeOtp, opts, NullLogger<UnreadNotificationScheduler>.Instance);
            try
            {
                await messages.MarkReadAsync(msg.Id, "bob", null);

                await InvokeOnTimerAsync(scheduler, msg.Id);

                Assert.True(fakeOtp.Calls.IsEmpty, "No notifications should be sent when message is read");
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
            }
        }

        [Fact]
        public async Task OnTimerAsync_DoesNotSendDuplicateNotificationsForSameMessage()
        {
            var (rooms, users, messages) = SetupRepos();
            await users.UpsertAsync(MkUser("alice"));
            await users.UpsertAsync(MkUser("bob"));

            var room = new Room
            {
                Name = "pair:dc-a::dc-b",
                DisplayName = "A <-> B",
                PairKey = "dc-a::dc-b",
                DispatchCenterAId = "dc-a",
                DispatchCenterBId = "dc-b",
                IsActive = true,
                Users = new List<string> { "alice", "bob" }
            };
            await rooms.UpsertAsync(room);
            var msg = await messages.CreateAsync(new Message
            {
                Content = "Hello again",
                FromUser = await users.GetByUserNameAsync("alice"),
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            });

            var fakeOtp = new FakeOtpSender();
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fakeOtp, opts, NullLogger<UnreadNotificationScheduler>.Instance);
            try
            {
                await InvokeOnTimerAsync(scheduler, msg.Id);
                await InvokeOnTimerAsync(scheduler, msg.Id);

                Assert.Equal(2, fakeOtp.Calls.Count);
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
            }
        }

        private static async Task InvokeOnTimerAsync(UnreadNotificationScheduler scheduler, int messageId)
        {
            var method = typeof(UnreadNotificationScheduler).GetMethod("OnTimerAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = method!.Invoke(scheduler, new object[] { messageId }) as Task;
            Assert.NotNull(task);
            await task!;
        }
    }
}
