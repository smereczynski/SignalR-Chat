using System;
using System.ComponentModel.DataAnnotations;

namespace Chat.Web.ViewModels
{
    /// <summary>
    /// API + SignalR projection of a message delivered to clients (adds correlation for optimistic UI reconciliation).
    /// </summary>
    public class MessageViewModel
    {
        public int Id { get; set; }
        [Required]
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string FromUserName { get; set; }
        public string FromFullName { get; set; }
        [Required]
        public string Room { get; set; }
        public string Avatar { get; set; }
        /// <summary>
        /// Optional client-supplied unique identifier (temporary) used to reconcile optimistic messages with server echo.
        /// </summary>
        public string CorrelationId { get; set; }
        /// <summary>
        /// Optional list of usernames who have read the message.
        /// </summary>
        public string[] ReadBy { get; set; }
        /// <summary>
        /// Translation status (None, Pending, InProgress, Completed, Failed).
        /// </summary>
        public string TranslationStatus { get; set; }
        /// <summary>
        /// Translated versions of the message (key: language code, value: translated text).
        /// Example: { "en": "Hello", "pl": "Cześć", "de": "Hallo" }
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Translations { get; set; }
        /// <summary>
        /// True if translation is completed successfully.
        /// </summary>
        public bool IsTranslated { get; set; }
    }
}
