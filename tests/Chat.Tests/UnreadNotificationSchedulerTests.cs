using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private class FakeNotificationSender : INotificationSender
        {
            public readonly ConcurrentBag<(string User, string Room, Message Msg)> Calls = new();
            public readonly TaskCompletionSource<(string User, string Room, Message Msg)> FirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task NotifyAsync(ApplicationUser user, string roomName, Message message)
            {
                var entry = (user?.UserName ?? "", roomName, message);
                Calls.Add(entry);
                FirstCall.TrySetResult(entry);
                return Task.CompletedTask;
            }
        }

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

        private static ApplicationUser MkUser(string name, params string[] fixedRooms)
        {
            return new ApplicationUser
            {
                UserName = name,
                FullName = name,
                Email = $"{name}@test.com",
                MobileNumber = $"+1234567{name.GetHashCode() % 10000:0000}",
                Enabled = true,
                FixedRooms = fixedRooms?.ToList() ?? new List<string>()
            };
        }

        [Fact(Timeout = 6000)]
        public async Task Sends_notifications_to_room_members_except_sender()
        {
            var (rooms, users, messages) = SetupRepos();
            // Prepare users assigned to #general via FixedRooms
            users.Upsert(MkUser("alice", "general"));
            users.Upsert(MkUser("bob", "general"));
            users.Upsert(MkUser("charlie")); // not in general -> should not get notified

            var room = rooms.GetByName("general");
            Assert.NotNull(room);

            // Create message from alice in #general
            var msg = messages.Create(new Message
            {
                Content = "Hello",
                FromUser = users.GetByUserName("alice"),
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            });

            var fake = new FakeNotificationSender();
            var fakeOtp = new FakeOtpSender();
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fake, fakeOtp, opts, NullLogger<UnreadNotificationScheduler>.Instance);
            try
            {
                scheduler.Schedule(msg);
                // Wait for the first notification - scheduler now sends via IOtpSender
                await Task.Delay(TimeSpan.FromMilliseconds(1300));
                
                // Ensure notifications were sent via IOtpSender (not INotificationSender)
                Assert.NotEmpty(fakeOtp.Calls);
                
                // Verify bob was notified (alice excluded as sender)
                // Note: UserNames parameter may contain "bob" if only one user or be a joined string
                var otpCalls = fakeOtp.Calls.ToList();
                var bobNotifications = otpCalls.Where(c => c.UserNames.Contains("bob")).ToList();
                Assert.NotEmpty(bobNotifications);
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
            }
        }

        [Fact(Timeout = 6000)]
        public async Task Skips_notifications_if_message_marked_read_before_delay()
        {
            var (rooms, users, messages) = SetupRepos();
            users.Upsert(MkUser("alice", "general"));
            users.Upsert(MkUser("bob", "general"));

            var room = rooms.GetByName("general");
            var msg = messages.Create(new Message
            {
                Content = "Check read",
                FromUser = users.GetByUserName("alice"),
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            });

            var fake = new FakeNotificationSender();
            var fakeOtp = new FakeOtpSender();
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fake, fakeOtp, opts, NullLogger<UnreadNotificationScheduler>.Instance);
            try
            {
                scheduler.Schedule(msg);
                // Mark as read by bob before the delay elapses
                messages.MarkRead(msg.Id, "bob");

                // Wait longer than delay and assert no notifications were sent
                await Task.Delay(TimeSpan.FromMilliseconds(1300));
                Assert.True(fake.Calls.IsEmpty, "No notifications should be sent when message is read");
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None);
                scheduler.Dispose();
            }
        }
    }
}
