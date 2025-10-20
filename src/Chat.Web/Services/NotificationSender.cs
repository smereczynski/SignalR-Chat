using System;
using System.Threading.Tasks;
using Chat.Web.Models;
using Microsoft.Extensions.Logging;
using Chat.Web.Repositories; // LogSanitizer

namespace Chat.Web.Services
{
    /// <summary>
    /// Default notification sender implementation. Uses the existing IOtpSender plumbing to reach email/SMS
    /// destinations, formatting bodies appropriately.
    /// </summary>
    public class NotificationSender : INotificationSender
    {
        private readonly IOtpSender _otpSender;
        private readonly ILogger<NotificationSender> _logger;

        public NotificationSender(IOtpSender otpSender, ILogger<NotificationSender> logger)
        {
            _otpSender = otpSender;
            _logger = logger;
        }

        public async Task NotifyAsync(ApplicationUser user, string roomName, Message message)
        {
            if (user == null) return;
            // Per requirements: do not include any message content or prefixes.
            // Body for both SMS and email must be exactly: "New message in #<channel>"
            var code = $"New message in #{roomName}";
            // Reuse IOtpSender routing: destination contains '@' => email; else SMS
            // Send to both email and mobile if available
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                await SafeSend(user.UserName, user.Email, code);
                _logger.LogInformation("Notification (email) queued to {Email} for user {User}", LogSanitizer.MaskEmail(user.Email), user.UserName);
            }
            if (!string.IsNullOrWhiteSpace(user.MobileNumber))
            {
                await SafeSend(user.UserName, user.MobileNumber, code);
                _logger.LogInformation("Notification (sms) queued to {Phone} for user {User}", LogSanitizer.MaskPhone(user.MobileNumber), user.UserName);
            }
        }

        private async Task SafeSend(string userName, string destination, string body)
        {
            try { await _otpSender.SendAsync(userName, destination, body); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Notification send failed to {Dest} for {User}", LogSanitizer.MaskDestination(destination), userName); }
        }
    }
}
