using System;
using System.Threading.Tasks;

namespace Chat.Web.Services
{
    /// <summary>
    /// Abstraction for ephemeral OTP code storage with per-user TTL.
    /// Implementations: in-memory (tests) and Redis (production).
    /// </summary>
    public interface IOtpStore
    {
        Task SetAsync(string userName, string code, TimeSpan ttl);
        Task<string> GetAsync(string userName);
        Task RemoveAsync(string userName);
        
        /// <summary>
        /// Increments the failed verification attempt counter for the user.
        /// Sets TTL on first increment to match OTP expiry.
        /// </summary>
        Task<int> IncrementAttemptsAsync(string userName, TimeSpan ttl);
        
        /// <summary>
        /// Gets the current failed verification attempt count for the user.
        /// Returns 0 if no attempts recorded or counter expired.
        /// </summary>
        Task<int> GetAttemptsAsync(string userName);
    }
}
