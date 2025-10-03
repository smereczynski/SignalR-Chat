using System;
using System.Threading.Tasks;

namespace Chat.Web.Services
{
    public class ConsoleOtpSender : IOtpSender
    {
        public Task SendAsync(string userName, string destination, string code)
        {
            Console.WriteLine($"[OTP] User={userName} Dest={destination} Code={code}");
            return Task.CompletedTask;
        }
    }
}
