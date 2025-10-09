using System;

namespace Chat.Web.Services
{
    /// <summary>
    /// Abstraction for hashing and verifying one-time passwords (OTPs) with support for versioning/rotation.
    /// Implementations must be deterministic for a given (pepper, userName, salt, code, params) tuple
    /// and use constant-time comparison on verification.
    /// </summary>
    public interface IOtpHasher
    {
        /// <summary>
        /// Computes a versioned hash string for the provided user and OTP code.
        /// The returned value is suitable for storage in the OTP store.
        /// </summary>
        string Hash(string userName, string code);

        /// <summary>
        /// Verifies the user/code against a stored value.
        /// </summary>
        VerificationResult Verify(string userName, string code, string stored);
    }

    public readonly struct VerificationResult
    {
        public bool IsMatch { get; }
        public bool NeedsRehash { get; }

        public VerificationResult(bool isMatch, bool needsRehash)
        {
            IsMatch = isMatch;
            NeedsRehash = needsRehash;
        }
    }
}
