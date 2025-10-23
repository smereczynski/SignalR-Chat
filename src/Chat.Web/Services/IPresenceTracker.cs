using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.ViewModels;

namespace Chat.Web.Services
{
    /// <summary>
    /// Distributed presence tracking service for multi-instance deployments.
    /// Ensures user presence state is synchronized across all app instances via Redis.
    /// </summary>
    public interface IPresenceTracker
    {
        /// <summary>
        /// Marks a user as online in a specific room.
        /// </summary>
        Task UserJoinedRoomAsync(string userName, string fullName, string avatar, string roomName);

        /// <summary>
        /// Removes a user from a specific room.
        /// </summary>
        Task UserLeftRoomAsync(string userName, string roomName);

        /// <summary>
        /// Completely removes a user from all rooms (on disconnect).
        /// </summary>
        Task UserDisconnectedAsync(string userName);

        /// <summary>
        /// Gets all users currently in a specific room across all instances.
        /// </summary>
        Task<IReadOnlyList<UserViewModel>> GetUsersInRoomAsync(string roomName);

        /// <summary>
        /// Gets a snapshot of all users and their current rooms across all instances.
        /// </summary>
        Task<IReadOnlyList<UserViewModel>> GetAllUsersAsync();

        /// <summary>
        /// Updates the user's connection ID (for targeted messaging).
        /// </summary>
        Task UpdateConnectionIdAsync(string userName, string connectionId);

        /// <summary>
        /// Gets the connection ID for a specific user (if online).
        /// </summary>
        Task<string> GetConnectionIdAsync(string userName);
    }
}
