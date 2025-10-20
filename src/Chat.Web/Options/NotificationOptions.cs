using System;

namespace Chat.Web.Options
{
    /// <summary>
    /// Options for chat notifications (e.g., unread message reminders).
    /// </summary>
    public class NotificationOptions
    {
        /// <summary>
        /// Delay in seconds between message delivery and sending unread notifications.
        /// If zero or negative, notifications are disabled. Defaults to 60 seconds.
        /// </summary>
        public int UnreadDelaySeconds { get; set; } = 60;
    }
}
