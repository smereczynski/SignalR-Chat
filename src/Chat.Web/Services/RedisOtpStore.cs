using System;
using System.Threading.Tasks;
using Chat.Web.Options;
using StackExchange.Redis;

namespace Chat.Web.Services
{
    public class RedisOtpStore : IOtpStore
    {
        private readonly IDatabase _db;
        private const string Prefix = "otp:";

        public RedisOtpStore(IConnectionMultiplexer mux, RedisOptions options)
        {
            _db = mux.GetDatabase(options.Database);
        }

        public async Task<string> GetAsync(string userName)
        {
            var val = await _db.StringGetAsync(Prefix + userName);
            return val.IsNullOrEmpty ? null : (string)val;
        }

        public Task RemoveAsync(string userName)
        {
            return _db.KeyDeleteAsync(Prefix + userName);
        }

        public Task SetAsync(string userName, string code, TimeSpan ttl)
        {
            return _db.StringSetAsync(Prefix + userName, code, ttl);
        }
    }
}
