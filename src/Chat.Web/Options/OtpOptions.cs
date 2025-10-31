using System;

namespace Chat.Web.Options
{
    public class OtpOptions
    {
        /// <summary>
        /// Base64-encoded pepper bytes. Keep high entropy (>= 32 bytes) and load from environment.
        /// </summary>
        public string Pepper { get; set; }

        /// <summary>
        /// Enable hashing path. When false, plaintext OTPs are stored (legacy/testing only).
        /// </summary>
        public bool HashingEnabled { get; set; } = true;

        /// <summary>
        /// Maximum number of failed verification attempts before blocking further attempts.
        /// Default: 5 attempts.
        /// </summary>
        public int MaxAttempts { get; set; } = 5;

        // Argon2id parameters (memory in KB as commonly used by libraries)
        public int MemoryKB { get; set; } = 64 * 1024; // 64 MB
        public int Iterations { get; set; } = 3;
        public int Parallelism { get; set; } = 1;
        public int OutputLength { get; set; } = 32; // bytes
    }
}
