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
    // Static rooms: interface intentionally minimal (read-only)
    }
}
