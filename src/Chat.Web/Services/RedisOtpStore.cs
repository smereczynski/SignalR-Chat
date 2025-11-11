using System;
using System.Threading.Tasks;
using Chat.Web.Options;
using Chat.Web.Resilience;
using Chat.Web.Utilities;
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
        private const string AttemptsPrefix = "otp_attempts:";
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
                _logger.LogWarning("Skipping Redis GET for OTP due to cooldown (active until {Until}). User: {UserName}", _cooldownUntil, LogSanitizer.Sanitize(userName));
                return null;
            }
            RedisValue val;
            try
            {
                _logger.LogDebug("Getting OTP from Redis for user: {UserName}", LogSanitizer.Sanitize(userName));
                val = await RetryHelper.ExecuteAsync(
                    _ => _db.StringGetAsync(Prefix + userName),
                    Transient.IsRedisTransient,
                    _logger,
                    "redis.otp.get",
                    maxAttempts: 3,
                    baseDelayMs: 200,
                    perAttemptTimeoutMs: 1500);
                
                if (val.IsNullOrEmpty)
                {
                    _logger.LogDebug("No OTP found in Redis for user: {UserName}", LogSanitizer.Sanitize(userName));
                }
                else
                {
                    _logger.LogDebug("OTP retrieved successfully from Redis for user: {UserName}", LogSanitizer.Sanitize(userName));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis GET failed for OTP store (user: {UserName}). Error: {ErrorType} - {Message}. Entering {Cooldown}s cooldown.",
                    LogSanitizer.Sanitize(userName), ex.GetType().Name, LogSanitizer.Sanitize(ex.Message), CooldownSeconds);
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
                _logger.LogWarning("Skipping Redis SET for OTP due to cooldown (active until {Until}). User: {UserName}", _cooldownUntil, LogSanitizer.Sanitize(userName));
                return Task.CompletedTask;
            }
            
            _logger.LogDebug("Setting OTP in Redis for user: {UserName}, TTL: {Ttl}s", LogSanitizer.Sanitize(userName), ttl.TotalSeconds);
            
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
                        _logger.LogError(t.Exception?.GetBaseException(), "Redis SET failed for OTP store (user: {UserName}). Error: {ErrorType} - {Message}. Entering {Cooldown}s cooldown.",
                            LogSanitizer.Sanitize(userName), t.Exception?.GetBaseException()?.GetType().Name, LogSanitizer.Sanitize(t.Exception?.GetBaseException()?.Message), CooldownSeconds);
                        ArmCooldown();
                    }
                    else
                    {
                        _logger.LogDebug("OTP set successfully in Redis for user: {UserName}", LogSanitizer.Sanitize(userName));
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

        /// <inheritdoc />
        public async Task<int> IncrementAttemptsAsync(string userName, TimeSpan ttl)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
            {
                _logger?.LogWarning("Skipping Redis INCR due to cooldown until {Until}", _cooldownUntil);
                return 0;
            }
            
            try
            {
                var key = AttemptsPrefix + userName;
                var count = await RetryHelper.ExecuteAsync(
                    _ => _db.StringIncrementAsync(key),
                    Transient.IsRedisTransient,
                    _logger,
                    "redis.otp.incr_attempts",
                    maxAttempts: 3,
                    baseDelayMs: 200,
                    perAttemptTimeoutMs: 1500);
                
                // Set TTL only on first increment (when count is 1)
                if (count == 1)
                {
                    await RetryHelper.ExecuteAsync(
                        _ => _db.KeyExpireAsync(key, ttl),
                        Transient.IsRedisTransient,
                        _logger,
                        "redis.otp.expire_attempts",
                        maxAttempts: 3,
                        baseDelayMs: 200,
                        perAttemptTimeoutMs: 1500);
                }
                
                return (int)count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Redis INCR failed for OTP attempts; entering cooldown");
                ArmCooldown();
                return 0; // Safe fallback - allow attempt rather than block
            }
        }

        /// <inheritdoc />
        public async Task<int> GetAttemptsAsync(string userName)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
            {
                _logger?.LogWarning("Skipping Redis GET due to cooldown until {Until}", _cooldownUntil);
                return 0; // Safe fallback - allow attempt rather than block
            }
            
            try
            {
                var val = await RetryHelper.ExecuteAsync(
                    _ => _db.StringGetAsync(AttemptsPrefix + userName),
                    Transient.IsRedisTransient,
                    _logger,
                    "redis.otp.get_attempts",
                    maxAttempts: 3,
                    baseDelayMs: 200,
                    perAttemptTimeoutMs: 1500);
                
                return val.IsNullOrEmpty ? 0 : (int)val;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Redis GET failed for OTP attempts; entering cooldown");
                ArmCooldown();
                return 0; // Safe fallback - allow attempt rather than block
            }
        }
    }
}
