using System;
using System.Threading.Tasks;
using Chat.Web.Repositories; // LogSanitizer
using Microsoft.Extensions.Localization;

namespace Chat.Web.Services
{
    /// <summary>
    /// Dev-friendly OTP sender: writes codes to standard output (non-secure, for local/testing only).
    /// Accepts IStringLocalizer for consistency with AcsOtpSender, though it's not used in console output.
    /// </summary>
    public class ConsoleOtpSender : IOtpSender
    {
        private readonly IStringLocalizer<Resources.SharedResources> _localizer;

        public ConsoleOtpSender(IStringLocalizer<Resources.SharedResources> localizer)
        {
            _localizer = localizer;
        }

        public Task SendAsync(string userName, string destination, string code)
        {
            var masked = LogSanitizer.MaskDestination(destination);
            Console.WriteLine($"[OTP] User={userName} Dest={masked} Code={code}");
            return Task.CompletedTask;
        }
    }
}
