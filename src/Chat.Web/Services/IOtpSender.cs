using System.Threading.Tasks;

namespace Chat.Web.Services
{
    /// <summary>
    /// Abstraction for delivering OTP codes to a user (email, SMS, console, etc.).
    /// </summary>
    public interface IOtpSender
    {
        Task SendAsync(string userName, string destination, string code);
    }
}
