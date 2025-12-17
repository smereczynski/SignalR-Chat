using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for persisting and querying chat messages (room-scoped with simple backward pagination support).
    /// </summary>
    public interface IMessagesRepository
    {
        Task<Message> GetByIdAsync(int id);
        Task<IEnumerable<Message>> GetRecentByRoomAsync(string roomName, int take = 20);
        Task<IEnumerable<Message>> GetBeforeByRoomAsync(string roomName, System.DateTime before, int take = 20);
        Task<Message> CreateAsync(Message message);
        Task DeleteAsync(int id, string byUserName);
        /// <summary>
        /// Marks a message as read by the specified user. Returns the updated message or null if not found.
        /// </summary>
        Task<Message> MarkReadAsync(int id, string userName);
        /// <summary>
        /// Updates translation status and translations for a message. Returns the updated message or null if not found.
        /// </summary>
        Task<Message> UpdateTranslationAsync(int id, MessageTranslationUpdate update);
    }
}
