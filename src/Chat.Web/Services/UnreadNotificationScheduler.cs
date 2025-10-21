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
    private readonly INotificationSender _notifier;
    private readonly IOtpSender _otpSender;
        private readonly IOptions<NotificationOptions> _options;
        private readonly ILogger<UnreadNotificationScheduler> _logger;
        private readonly ConcurrentDictionary<int, Timer> _timers = new();
        private readonly ConcurrentDictionary<int, bool> _notificationsSent = new(); // Track which messages have been notified

        public UnreadNotificationScheduler(IRoomsRepository rooms, IUsersRepository users, IMessagesRepository messages, INotificationSender notifier, IOtpSender otpSender, IOptions<NotificationOptions> options, ILogger<UnreadNotificationScheduler> logger)
        {
            _rooms = rooms;
            _users = users;
            _messages = messages;
            _notifier = notifier;
            _otpSender = otpSender;
            _options = options;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var kv in _timers.ToArray())
            {
                try { kv.Value?.Dispose(); } catch { }
            }
            _timers.Clear();
            _notificationsSent.Clear();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var kv in _timers.ToArray())
            {
                try { kv.Value?.Dispose(); } catch { }
            }
            _timers.Clear();
            _notificationsSent.Clear();
        }

        /// <summary>
        /// Cancel a scheduled notification for the specified message (e.g., when marked as read).
        /// </summary>
        public void Cancel(int messageId)
        {
            if (_timers.TryRemove(messageId, out var timer))
            {
                try
                {
                    timer?.Dispose();
                    _logger.LogDebug("Cancelled unread notification timer for message {Id}", messageId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing timer for message {Id}", messageId);
                }
            }
        }

        /// <summary>
        /// Schedule a notification check for the specified message.
        /// </summary>
        public void Schedule(Message message)
        {
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
                try { timer.Dispose(); } catch { }
            }
            else
            {
                _logger.LogDebug("Scheduled unread notification for message {Id} in {Delay}s", message.Id, delaySec);
            }
        }

        private async Task OnTimerAsync(int messageId)
        {
            if (!_timers.TryRemove(messageId, out var t)) { }
            try { t?.Dispose(); } catch { }
            
            // Check if notification was already sent for this message
            if (_notificationsSent.ContainsKey(messageId))
            {
                _logger.LogDebug("Notification already sent for message {Id}, skipping", messageId);
                return;
            }
            
            try
            {
                var msg = _messages.GetById(messageId);
                if (msg == null) return;
                var room = msg.ToRoom ?? _rooms.GetById(msg.ToRoomId);
                var roomName = room?.Name ?? msg.ToRoom?.Name;
                if (string.IsNullOrWhiteSpace(roomName)) return;

                // Readers set (lowercased)
                var readers = new HashSet<string>((msg.ReadBy ?? Array.Empty<string>()).Select(u => u?.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
                
                // If message has been read by anyone, cancel the notification
                if (readers.Count > 0)
                {
                    _logger.LogDebug("Message {Id} has been read by {Count} users, skipping notification", messageId, readers.Count);
                    return;
                }
                
                // Gather target users assigned to room
                var userNames = (room?.Users ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
                if (userNames.Count == 0)
                {
                    // Fallback: if room.Users is not populated, infer from users whose FixedRooms include this room
                    var inferred = _users.GetAll()
                        ?.Where(u => u != null && u.Enabled != false && (u.FixedRooms?.Contains(roomName, StringComparer.OrdinalIgnoreCase) ?? false))
                        ?.Select(u => u.UserName)
                        ?.Where(n => !string.IsNullOrWhiteSpace(n))
                        ?.Distinct(StringComparer.OrdinalIgnoreCase)
                        ?.ToList() ?? new List<string>();
                    if (inferred.Count > 0)
                    {
                        userNames = inferred;
                        _logger.LogDebug("Recipients inferred from FixedRooms for room {Room}: {Count}", roomName, userNames.Count);
                    }
                    else
                    {
                        _logger.LogDebug("No recipients found for room {Room} (room.Users empty and no users with FixedRooms). Skipping notification for message {Id}", roomName, messageId);
                        return;
                    }
                }
                // Exclude sender only (don't exclude readers since we already checked if anyone read it above)
                var toNotify = userNames.Where(u => !string.Equals(u, msg.FromUser?.UserName, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (toNotify.Count == 0) return;

                // Mark as sent before sending to prevent duplicate sends if multiple timers fire
                _notificationsSent.TryAdd(messageId, true);

                // Aggregate unique destinations (email / mobile) across users to avoid duplicate sends to same address/number
                var emailDests = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // dest -> usernames
                var smsDests = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var uname in toNotify)
                {
                    var user = _users.GetByUserName(uname);
                    if (user == null || user.Enabled == false) continue;
                    if (!string.IsNullOrWhiteSpace(user.Email))
                    {
                        if (!emailDests.TryGetValue(user.Email, out var list)) { list = new List<string>(); emailDests[user.Email] = list; }
                        list.Add(user.UserName);
                    }
                    if (!string.IsNullOrWhiteSpace(user.MobileNumber))
                    {
                        if (!smsDests.TryGetValue(user.MobileNumber, out var list)) { list = new List<string>(); smsDests[user.MobileNumber] = list; }
                        list.Add(user.UserName);
                    }
                }

                // Prepare message body
                var body = $"New message in #{roomName}";

                // Send to unique email destinations once
                foreach (var kv in emailDests)
                {
                    try
                    {
                        await _otpSender.SendAsync(string.Join(',', kv.Value), kv.Key, body).ConfigureAwait(false);
                        _logger.LogInformation("Unread notification (email) queued for destination (users: {Users})", string.Join(',', kv.Value));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send unread email notification for destination");
                    }
                }

                // Send to unique SMS destinations once
                foreach (var kv in smsDests)
                {
                    try
                    {
                        await _otpSender.SendAsync(string.Join(',', kv.Value), kv.Key, body).ConfigureAwait(false);
                        _logger.LogInformation("Unread notification (sms) queued for destination (users: {Users})", string.Join(',', kv.Value));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send unread sms notification for destination");
                    }
                }

                _logger.LogInformation("Unread notifications processed for message {Id}; target user count: {Count}, email destinations: {Emails}, sms destinations: {Sms}", messageId, toNotify.Count, emailDests.Count, smsDests.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unread notification timer failed for message {Id}", messageId);
            }
        }
    }
}
