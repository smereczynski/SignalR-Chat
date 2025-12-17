using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chat.Web.Models
{
    /// <summary>
    /// Represents a chat message posted to a room (FromUser -> Room) with a server-side timestamp.
    /// Supports asynchronous translation with status tracking.
    /// </summary>
    public class Message
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public ApplicationUser FromUser { get; set; }
        public int ToRoomId { get; set; }
        public Room ToRoom { get; set; }
        // Users who have read this message (usernames)
        public ICollection<string> ReadBy { get; set; } = new List<string>();
        
        /// <summary>
        /// Current translation status (None, Pending, InProgress, Completed, Failed).
        /// </summary>
        public TranslationStatus TranslationStatus { get; set; } = TranslationStatus.None;
        
        /// <summary>
        /// Translated versions of the message content (key: language code, value: translated text).
        /// Always includes "en" (English) when translation is completed.
        /// Example: { "en": "Hello", "pl": "Cześć", "de": "Hallo" }
        /// </summary>
        public Dictionary<string, string> Translations { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// Redis job ID for tracking translation job (format: "transjob:{messageId}").
        /// Null if no translation job exists.
        /// </summary>
        public string TranslationJobId { get; set; }
        
        /// <summary>
        /// Timestamp when translation failed (null if not failed).
        /// Used to determine if retry should be attempted.
        /// </summary>
        public DateTime? TranslationFailedAt { get; set; }

        /// <summary>
        /// High-level category of why translation failed.
        /// </summary>
        public TranslationFailureCategory TranslationFailureCategory { get; set; } = TranslationFailureCategory.Unknown;

        /// <summary>
        /// Machine-readable failure code to help troubleshooting.
        /// </summary>
        public TranslationFailureCode TranslationFailureCode { get; set; } = TranslationFailureCode.Unknown;

        /// <summary>
        /// Safe, user-displayable failure message (never includes user-provided text).
        /// </summary>
        public string TranslationFailureMessage { get; set; }
        
        /// <summary>
        /// Computed property: returns true if translation is completed successfully.
        /// </summary>
        public bool IsTranslated => TranslationStatus == TranslationStatus.Completed && Translations.Count > 0;
    }
}
