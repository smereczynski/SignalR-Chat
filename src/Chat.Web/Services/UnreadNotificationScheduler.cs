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
        public void CancelNotification(int messageId)
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
                var msg = await _messages.GetByIdAsync(messageId);
                if (msg == null) return;
                var room = msg.ToRoom ?? await _rooms.GetByIdAsync(msg.ToRoomId);
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
                
                // Gather ALL users assigned to the room from FixedRooms (source of truth for room membership)
                // Note: room.Users contains only currently connected users (presence tracking), not all assigned users
                var userNames = (await _users.GetAllAsync())
                    ?.Where(u => u != null && u.Enabled != false && (u.FixedRooms?.Contains(roomName, StringComparer.OrdinalIgnoreCase) ?? false))
                    ?.Select(u => u.UserName)
                    ?.Where(n => !string.IsNullOrWhiteSpace(n))
                    ?.Distinct(StringComparer.OrdinalIgnoreCase)
                    ?.ToList() ?? new List<string>();
                
                if (userNames.Count == 0)
                {
                    _logger.LogDebug("No recipients found for room {Room} (no users with FixedRooms containing this room). Skipping notification for message {Id}", roomName, messageId);
                    return;
                }
                
                _logger.LogDebug("Found {Count} recipients for room {Room} from FixedRooms", userNames.Count, roomName);
                
                // Exclude sender only (don't exclude readers since we already checked if anyone read it above)
                var toNotify = userNames.Where(u => !string.Equals(u, msg.FromUser?.UserName, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (toNotify.Count == 0) return;

                _logger.LogInformation("Unread notification check for message {Id}: {UserCount} recipients to notify (excluding sender)", 
                    messageId, toNotify.Count);

                // Mark as sent before sending to prevent duplicate sends if multiple timers fire
                _notificationsSent.TryAdd(messageId, true);

                // Prepare message body
                var body = $"New message in #{roomName}";

                // Send notifications to each user individually (no deduplication by email/phone)
                var emailsSent = 0;
                var smsSent = 0;
                
                foreach (var uname in toNotify)
                {
                    var user = await _users.GetByUserNameAsync(uname);
                    if (user == null || user.Enabled == false) continue;
                    
                    // Send email if user has one
                    if (!string.IsNullOrWhiteSpace(user.Email))
                    {
                        try
                        {
                            await _otpSender.SendAsync(user.UserName, user.Email, body).ConfigureAwait(false);
                            emailsSent++;
                            _logger.LogInformation("Unread notification (email) queued");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send unread email notification");
                        }
                    }
                    
                    // Send SMS if user has one
                    if (!string.IsNullOrWhiteSpace(user.MobileNumber))
                    {
                        try
                        {
                            await _otpSender.SendAsync(user.UserName, user.MobileNumber, body).ConfigureAwait(false);
                            smsSent++;
                            _logger.LogInformation("Unread notification (sms) queued");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send unread sms notification");
                        }
                    }
                }

                _logger.LogInformation("Unread notifications processed for message {Id}: {UserCount} users, {EmailCount} emails sent, {SmsCount} SMS sent", 
                    messageId, toNotify.Count, emailsSent, smsSent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unread notification timer failed for message {Id}", messageId);
            }
        }
    }
}
