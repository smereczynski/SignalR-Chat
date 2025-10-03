using System.Threading.Tasks;

namespace Chat.Web.Services
{
    public interface IOtpSender
    {
        Task SendAsync(string userName, string destination, string code);
    }
}
