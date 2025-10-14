using System.Collections.Generic;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for chat room storage / retrieval. Implementations may map integer IDs to provider-native keys.
    /// </summary>
    public interface IRoomsRepository
    {
        IEnumerable<Room> GetAll();
        Room GetById(int id);
        Room GetByName(string name);
        // Maintain denormalized user membership in room document
        void AddUserToRoom(string roomName, string userName);
        void RemoveUserFromRoom(string roomName, string userName);
    // Static rooms: names/ids are fixed; membership is maintained
    }
}
