using System.Threading.Tasks;

namespace Chat.Web.Services
{
    /// <summary>
    /// Abstraction for sending user notifications (email/SMS) for unread messages.
    /// </summary>
    public interface INotificationSender
    {
        Task NotifyAsync(Models.ApplicationUser user, string roomName, Models.Message message);
    }
}
