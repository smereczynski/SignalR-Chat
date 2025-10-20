using System.Collections.Generic;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for persisting and querying chat messages (room-scoped with simple backward pagination support).
    /// </summary>
    public interface IMessagesRepository
    {
        Message GetById(int id);
        IEnumerable<Message> GetRecentByRoom(string roomName, int take = 20);
        IEnumerable<Message> GetBeforeByRoom(string roomName, System.DateTime before, int take = 20);
        Message Create(Message message);
        void Delete(int id, string byUserName);
        /// <summary>
        /// Marks a message as read by the specified user. Returns the updated message or null if not found.
        /// </summary>
        Message MarkRead(int id, string userName);
    }
}
