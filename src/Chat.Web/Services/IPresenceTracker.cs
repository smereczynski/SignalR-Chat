using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.ViewModels;

namespace Chat.Web.Services
{
    /// <summary>
    /// Simplified presence tracking for distributed SignalR deployments.
    /// Stores user → room mappings in Redis. Per-connection state uses Context.Items.
    /// </summary>
    public interface IPresenceTracker
    {
        /// <summary>
        /// Sets the user's current room and profile in distributed storage.
        /// Pass empty roomName to indicate connected but not in a room.
        /// </summary>
        Task SetUserRoomAsync(string userName, string fullName, string avatar, string roomName);

        /// <summary>
        /// Gets a single user's presence information.
        /// </summary>
        Task<UserViewModel> GetUserAsync(string userName);

        /// <summary>
        /// Removes a user from distributed presence (on disconnect).
        /// </summary>
        Task RemoveUserAsync(string userName);

        /// <summary>
        /// Gets all users for presence snapshot API (not used in hot path).
        /// </summary>
        Task<IReadOnlyList<UserViewModel>> GetAllUsersAsync();
    }
}
