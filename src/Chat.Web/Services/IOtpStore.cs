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
    }
}
