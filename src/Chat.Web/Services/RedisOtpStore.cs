using System;
using System.Threading.Tasks;
using Chat.Web.Options;
using StackExchange.Redis;

namespace Chat.Web.Services
{
    /// <summary>
    /// Redis-backed OTP store (prefix 'otp:'). Values expire naturally via key TTL.
    /// </summary>
    public class RedisOtpStore : IOtpStore
    {
        private readonly IDatabase _db;
        private const string Prefix = "otp:";

    /// <summary>
    /// Constructs the store using a multiplexer and options specifying DB index.
    /// </summary>
    public RedisOtpStore(IConnectionMultiplexer mux, Microsoft.Extensions.Options.IOptions<RedisOptions> options)
        {
            var opts = options.Value;
            _db = mux.GetDatabase(opts.Database);
        }

    /// <inheritdoc />
    public async Task<string> GetAsync(string userName)
        {
            var val = await _db.StringGetAsync(Prefix + userName);
            return val.IsNullOrEmpty ? null : (string)val;
        }

        /// <inheritdoc />
        public Task RemoveAsync(string userName)
        {
            return _db.KeyDeleteAsync(Prefix + userName);
        }

        /// <inheritdoc />
        public Task SetAsync(string userName, string code, TimeSpan ttl)
        {
            return _db.StringSetAsync(Prefix + userName, code, ttl);
        }
    }
}
