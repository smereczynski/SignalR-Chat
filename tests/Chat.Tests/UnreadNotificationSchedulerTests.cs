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
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fake, opts, NullLogger<UnreadNotificationScheduler>.Instance);
            try
            {
                scheduler.Schedule(msg);
                // Wait for the first notification (expected: bob)
                var completed = await Task.WhenAny(fake.FirstCall.Task, Task.Delay(TimeSpan.FromSeconds(3)));
                Assert.Same(fake.FirstCall.Task, completed);
                var call = await fake.FirstCall.Task; // unwrap result
                Assert.Equal("general", call.Room);
                Assert.Equal(msg.Id, call.Msg.Id);
                Assert.Equal("bob", call.User); // alice excluded (sender)

                // Ensure only bob was notified
                var recipients = fake.Calls.Select(c => c.User).ToList();
                Assert.Contains("bob", recipients);
                Assert.DoesNotContain("alice", recipients);
                Assert.DoesNotContain("charlie", recipients);
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
            var opts = Options.Create(new NotificationOptions { UnreadDelaySeconds = 1 });
            var scheduler = new UnreadNotificationScheduler(rooms, users, messages, fake, opts, NullLogger<UnreadNotificationScheduler>.Instance);
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
