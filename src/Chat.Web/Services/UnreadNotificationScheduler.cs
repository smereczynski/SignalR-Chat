using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Options;
using Chat.Web.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Web.Services
{
    /// <summary>
    /// Schedules unread-message notifications: when a message is delivered to a room and remains unread by all assigned users
    /// for a configured delay, sends email and SMS notifications to those users.
    /// </summary>
    public class UnreadNotificationScheduler : IHostedService, IDisposable
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IMessagesRepository _messages;
        private readonly IOtpSender _otpSender;
        private readonly IOptions<NotificationOptions> _options;
        private readonly ILogger<UnreadNotificationScheduler> _logger;
        private readonly ConcurrentDictionary<int, Timer> _timers = new();
        private readonly ConcurrentDictionary<int, bool> _notificationsSent = new(); // Track which messages have been notified
        private bool _disposed;

        public UnreadNotificationScheduler(IRoomsRepository rooms, IUsersRepository users, IMessagesRepository messages, IOtpSender otpSender, IOptions<NotificationOptions> options, ILogger<UnreadNotificationScheduler> logger)
        {
            _rooms = rooms;
            _users = users;
            _messages = messages;
            _otpSender = otpSender;
            _options = options;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            DisposeManagedResources();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                DisposeManagedResources();
            }

            _disposed = true;
        }

        /// <summary>
        /// Cancel a scheduled notification for the specified message (e.g., when marked as read).
        /// </summary>
        public void CancelNotification(int messageId)
        {
            if (_timers.TryRemove(messageId, out var timer))
            {
                DisposeTimer(timer, messageId);
                _logger.LogDebug("Cancelled unread notification timer for message {Id}", messageId);
            }
        }

        /// <summary>
        /// Schedule a notification check for the specified message.
        /// </summary>
        public void Schedule(Message message)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (message == null || message.Id <= 0) return;
            var delaySec = Math.Max(0, _options.Value?.UnreadDelaySeconds ?? 0);
            if (delaySec <= 0)
            {
                _logger.LogDebug("Unread notifications disabled (delay <= 0). Skipping schedule for message {Id}", message.Id);
                return;
            }
            // If already scheduled, skip
            if (_timers.ContainsKey(message.Id)) return;

            var due = TimeSpan.FromSeconds(delaySec);
            var timer = new Timer(async _ => await OnTimerAsync(message.Id).ConfigureAwait(false), null, due, Timeout.InfiniteTimeSpan);
            if (!_timers.TryAdd(message.Id, timer))
            {
                DisposeTimer(timer, message.Id);
            }
            else
            {
                _logger.LogDebug("Scheduled unread notification for message {Id} in {Delay}s", message.Id, delaySec);
            }
        }

        private async Task OnTimerAsync(int messageId)
        {
            await ReleaseTimerAsync(messageId).ConfigureAwait(false);

            if (_notificationsSent.ContainsKey(messageId))
            {
                _logger.LogDebug("Notification already sent for message {Id}, skipping", messageId);
                return;
            }

            try
            {
                var notification = await BuildNotificationAsync(messageId).ConfigureAwait(false);
                if (notification == null)
                {
                    return;
                }

                var (emailsSent, smsSent) = await SendNotificationsAsync(notification).ConfigureAwait(false);
                _logger.LogInformation(
                    "Unread notifications processed for message {Id}: {UserCount} users",
                    messageId,
                    notification.Recipients.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unread notification timer failed for message {Id}", messageId);
            }
        }

        private void DisposeManagedResources()
        {
            foreach (var entry in _timers.ToArray())
            {
                DisposeTimer(entry.Value, entry.Key);
            }

            _timers.Clear();
            _notificationsSent.Clear();
        }

        private void DisposeTimer(Timer timer, int messageId)
        {
            try
            {
                timer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing timer for message {Id}", messageId);
            }
        }

        private async Task ReleaseTimerAsync(int messageId)
        {
            if (_timers.TryRemove(messageId, out var timer))
            {
                await DisposeTimerAsync(timer, messageId).ConfigureAwait(false);
            }
        }

        private async ValueTask DisposeTimerAsync(Timer timer, int messageId)
        {
            try
            {
                await timer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing timer for message {Id}", messageId);
            }
        }

        private async Task<PendingUnreadNotification> BuildNotificationAsync(int messageId)
        {
            var message = await _messages.GetByIdAsync(messageId).ConfigureAwait(false);
            if (message == null)
            {
                return null;
            }

            var room = message.ToRoom ?? await _rooms.GetByIdAsync(message.ToRoomId).ConfigureAwait(false);
            if (room == null || string.IsNullOrWhiteSpace(room.Name))
            {
                return null;
            }

            if (HasReaders(message, messageId))
            {
                return null;
            }

            var recipients = await GetRecipientsAsync(room, room.Name, messageId, message.FromUser?.UserName).ConfigureAwait(false);
            if (recipients.Count == 0)
            {
                return null;
            }

            _notificationsSent.TryAdd(messageId, true);

            return new PendingUnreadNotification(messageId, room.Name, recipients);
        }

        private bool HasReaders(Message message, int messageId)
        {
            var readers = new HashSet<string>(message.ReadBy ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (readers.Count == 0)
            {
                return false;
            }

            _logger.LogDebug("Message {Id} has been read by {Count} users, skipping notification", messageId, readers.Count);
            return true;
        }

        private async Task<List<string>> GetRecipientsAsync(Room room, string roomName, int messageId, string senderUserName)
        {
            var roomUsers = room.Users ?? new List<string>();
            var allUsers = await _users.GetAllAsync().ConfigureAwait(false) ?? Enumerable.Empty<ApplicationUser>();
            var userNames = allUsers
                .Where(user => user != null && user.Enabled && roomUsers.Contains(user.UserName, StringComparer.OrdinalIgnoreCase))
                .Select(user => user.UserName)
                .Where(userName => !string.IsNullOrWhiteSpace(userName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (userNames.Count == 0)
            {
                _logger.LogDebug("No recipients found for room {Room}. Skipping notification for message {Id}", roomName, messageId);
                return userNames;
            }

            _logger.LogDebug("Found {Count} recipients for room {Room}", userNames.Count, roomName);

            var recipients = userNames
                .Where(userName => !string.Equals(userName, senderUserName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count > 0)
            {
                _logger.LogInformation(
                    "Unread notification check for message {Id}: {UserCount} recipients to notify (excluding sender)",
                    messageId,
                    recipients.Count);
            }

            return recipients;
        }

        private async Task<(int EmailsSent, int SmsSent)> SendNotificationsAsync(PendingUnreadNotification notification)
        {
            var body = $"New message in #{notification.RoomName}";
            var emailsSent = 0;
            var smsSent = 0;

            foreach (var userName in notification.Recipients)
            {
                var user = await _users.GetByUserNameAsync(userName).ConfigureAwait(false);
                if (user == null || !user.Enabled)
                {
                    continue;
                }

                emailsSent += await TrySendAsync(user.UserName, user.Email, body, "email").ConfigureAwait(false);
                smsSent += await TrySendAsync(user.UserName, user.MobileNumber, body, "sms").ConfigureAwait(false);
            }

            return (emailsSent, smsSent);
        }

        private async Task<int> TrySendAsync(string userName, string destination, string body, string channel)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                return 0;
            }

            try
            {
                await _otpSender.SendAsync(userName, destination, body).ConfigureAwait(false);
                _logger.LogInformation("Unread notification ({Channel}) queued", channel);
                return 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send unread {Channel} notification", channel);
                return 0;
            }
        }

        private sealed record PendingUnreadNotification(int MessageId, string RoomName, List<string> Recipients);
    }
}
