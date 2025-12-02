using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for chat room storage / retrieval. Implementations may map integer IDs to provider-native keys.
    /// </summary>
    public interface IRoomsRepository
    {
        Task<IEnumerable<Room>> GetAllAsync();
        Task<Room> GetByIdAsync(int id);
        Task<Room> GetByNameAsync(string name);
        // Maintain denormalized user membership in room document
        Task AddUserToRoomAsync(string roomName, string userName);
        Task RemoveUserFromRoomAsync(string roomName, string userName);
    // Static rooms: names/ids are fixed; membership is maintained
    }
}
