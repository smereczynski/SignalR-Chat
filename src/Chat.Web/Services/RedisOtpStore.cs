using System;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Chat.Web.Services
{
    /// <summary>
    /// Redis-backed OTP store (prefix 'otp:'). Values expire naturally via key TTL.
    /// </summary>
    public class RedisOtpStore : IOtpStore
    {
    private readonly IDatabase _db;
    private readonly ILogger<RedisOtpStore> _logger;
        private const string Prefix = "otp:";
        private static DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;
        private static readonly object _gate = new object();
        private const int CooldownSeconds = 10;

    /// <summary>
    /// Constructs the store using a multiplexer and options specifying DB index.
    /// </summary>
    public RedisOtpStore(IConnectionMultiplexer mux, Microsoft.Extensions.Options.IOptions<RedisOptions> options, ILogger<RedisOtpStore> logger)
        {
            var opts = options.Value;
            _db = mux.GetDatabase(opts.Database);
            _logger = logger;
        }

    /// <inheritdoc />
    public async Task<string> GetAsync(string userName)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
            {
                _logger?.LogWarning("Skipping Redis GET due to cooldown until {Until}", _cooldownUntil);
                return null;
            }
            RedisValue val;
            try
            {
                val = await RetryHelper.ExecuteAsync(
                    _ => _db.StringGetAsync(Prefix + userName),
                    Transient.IsRedisTransient,
                    _logger,
                    "redis.otp.get",
                    maxAttempts: 3,
                    baseDelayMs: 200,
                    perAttemptTimeoutMs: 1500);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Redis GET failed for OTP store; entering cooldown");
                ArmCooldown();
                throw;
            }
            return val.IsNullOrEmpty ? null : (string)val;
        }

        /// <inheritdoc />
        public Task RemoveAsync(string userName)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
            {
                _logger?.LogWarning("Skipping Redis DEL due to cooldown until {Until}", _cooldownUntil);
                return Task.CompletedTask;
            }
            return RetryHelper.ExecuteAsync(
                _ => _db.KeyDeleteAsync(Prefix + userName),
                Transient.IsRedisTransient,
                _logger,
                "redis.otp.remove",
                maxAttempts: 3,
                baseDelayMs: 200,
                perAttemptTimeoutMs: 1500).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger?.LogError(t.Exception?.GetBaseException(), "Redis DEL failed for OTP store; entering cooldown");
                        ArmCooldown();
                    }
                });
        }

        /// <inheritdoc />
        public Task SetAsync(string userName, string code, TimeSpan ttl)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
            {
                _logger?.LogWarning("Skipping Redis SET due to cooldown until {Until}", _cooldownUntil);
                return Task.CompletedTask;
            }
            return RetryHelper.ExecuteAsync(
                _ => _db.StringSetAsync(Prefix + userName, code, ttl),
                Transient.IsRedisTransient,
                _logger,
                "redis.otp.set",
                maxAttempts: 3,
                baseDelayMs: 200,
                perAttemptTimeoutMs: 1500).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger?.LogError(t.Exception?.GetBaseException(), "Redis SET failed for OTP store; entering cooldown");
                        ArmCooldown();
                    }
                });
        }

        private static void ArmCooldown()
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(CooldownSeconds);
            lock (_gate)
            {
                if (until > _cooldownUntil)
                {
                    _cooldownUntil = until;
                }
            }
        }
    }
}
