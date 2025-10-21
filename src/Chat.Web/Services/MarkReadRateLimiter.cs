using System;
using System.Collections.Concurrent;
using System.Threading;
using Chat.Web.Options;
using Microsoft.Extensions.Options;

namespace Chat.Web.Services
{
    /// <summary>
    /// Per-user rate limiter for MarkRead operations using a sliding window algorithm.
    /// Tracks operation timestamps per user and enforces configurable limits to prevent abuse.
    /// </summary>
    public interface IMarkReadRateLimiter
    {
        /// <summary>
        /// Attempts to acquire permission for a MarkRead operation for the specified user.
        /// </summary>
        /// <param name="userName">The user requesting the operation</param>
        /// <returns>True if allowed, false if rate limit exceeded</returns>
        bool TryAcquire(string userName);
    }

    /// <summary>
    /// Thread-safe sliding window rate limiter implementation.
    /// Uses ConcurrentDictionary to track per-user operation timestamps.
    /// </summary>
    public class MarkReadRateLimiter : IMarkReadRateLimiter
    {
        private readonly int _permitLimit;
        private readonly TimeSpan _window;
        private readonly ConcurrentDictionary<string, UserRateLimit> _userLimits = new(StringComparer.OrdinalIgnoreCase);

        public MarkReadRateLimiter(IOptions<RateLimitingOptions> options)
        {
            var opts = options.Value;
            _permitLimit = opts.MarkReadPermitLimit;
            _window = TimeSpan.FromSeconds(opts.MarkReadWindowSeconds);
        }

        public bool TryAcquire(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return false;

            var limit = _userLimits.GetOrAdd(userName, _ => new UserRateLimit());
            return limit.TryAcquire(_permitLimit, _window);
        }

        /// <summary>
        /// Per-user rate limit state with thread-safe sliding window tracking.
        /// </summary>
        private class UserRateLimit
        {
            private readonly object _lock = new object();
            private readonly System.Collections.Generic.Queue<DateTimeOffset> _timestamps = new();

            public bool TryAcquire(int permitLimit, TimeSpan window)
            {
                lock (_lock)
                {
                    var now = DateTimeOffset.UtcNow;
                    var cutoff = now - window;

                    // Remove expired timestamps outside the window
                    while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    {
                        _timestamps.Dequeue();
                    }

                    // Check if under limit
                    if (_timestamps.Count < permitLimit)
                    {
                        _timestamps.Enqueue(now);
                        return true;
                    }

                    return false;
                }
            }
        }
    }
}
