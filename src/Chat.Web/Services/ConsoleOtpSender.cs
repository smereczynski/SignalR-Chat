using System;
using System.Threading.Tasks;

namespace Chat.Web.Services
{
    /// <summary>
    /// Dev-friendly OTP sender: writes codes to standard output (non-secure, for local/testing only).
    /// </summary>
    public class ConsoleOtpSender : IOtpSender
    {
        public Task SendAsync(string userName, string destination, string code)
        {
            Console.WriteLine($"[OTP] User={userName} Dest={destination} Code={code}");
            return Task.CompletedTask;
        }
    }
}
