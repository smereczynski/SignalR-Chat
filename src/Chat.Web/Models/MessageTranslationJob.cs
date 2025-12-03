using System;
using System.Collections.Generic;

namespace Chat.Web.Models
{
    /// <summary>
    /// Represents a translation job for a message in the Redis queue.
    /// Serialized to JSON for storage in Redis List.
    /// </summary>
    public class MessageTranslationJob
    {
        /// <summary>
        /// Message ID from Cosmos DB.
        /// </summary>
        public int MessageId { get; set; }
        
        /// <summary>
        /// Room name where message was sent (needed for SignalR broadcast).
        /// </summary>
        public string RoomName { get; set; }
        
        /// <summary>
        /// Original message content to translate.
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// Source language code (e.g., "en", "pl", "auto" for auto-detect).
        /// </summary>
        public string SourceLanguage { get; set; }
        
        /// <summary>
        /// Target language codes to translate to.
        /// Always includes "en" (English).
        /// </summary>
        public List<string> TargetLanguages { get; set; } = new List<string>();
        
        /// <summary>
        /// Deployment name for Azure AI Translator (e.g., "gpt-4o-mini").
        /// </summary>
        public string DeploymentName { get; set; }
        
        /// <summary>
        /// Timestamp when job was created (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Number of times this job has been retried (starts at 0).
        /// </summary>
        public int RetryCount { get; set; } = 0;
        
        /// <summary>
        /// Priority level (higher = processed first). Default is 0.
        /// Manual retries can use higher priority.
        /// </summary>
        public int Priority { get; set; } = 0;
        
        /// <summary>
        /// Unique job ID for tracking in Redis (format: "transjob:{messageId}:{timestamp}").
        /// </summary>
        public string JobId { get; set; }
    }
}
