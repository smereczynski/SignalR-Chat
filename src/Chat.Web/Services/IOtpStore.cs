using System;
using System.Threading.Tasks;

namespace Chat.Web.Services
{
    public interface IOtpStore
    {
        Task SetAsync(string userName, string code, TimeSpan ttl);
        Task<string> GetAsync(string userName);
        Task RemoveAsync(string userName);
    }
}
