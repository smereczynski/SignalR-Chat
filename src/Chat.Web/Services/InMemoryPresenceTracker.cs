using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.ViewModels;

namespace Chat.Web.Services
{
    /// <summary>
    /// In-memory presence tracker for test/development environments.
    /// NOT suitable for multi-instance production use.
    /// </summary>
    public class InMemoryPresenceTracker : IPresenceTracker
    {
        private readonly ConcurrentDictionary<string, UserViewModel> _users = new();
        private readonly ConcurrentDictionary<string, string> _connectionMap = new();

        public Task UserJoinedRoomAsync(string userName, string fullName, string avatar, string roomName)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(roomName))
                return Task.CompletedTask;

            var user = new UserViewModel
            {
                UserName = userName,
                FullName = fullName ?? userName,
                Avatar = avatar,
                CurrentRoom = roomName
            };

            _users[userName] = user;
            return Task.CompletedTask;
        }

        public Task UserLeftRoomAsync(string userName, string roomName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return Task.CompletedTask;

            if (_users.TryGetValue(userName, out var user) && user.CurrentRoom == roomName)
            {
                user.CurrentRoom = string.Empty;
            }

            return Task.CompletedTask;
        }

        public Task UserDisconnectedAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return Task.CompletedTask;

            _users.TryRemove(userName, out _);
            _connectionMap.TryRemove(userName, out _);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserViewModel>> GetUsersInRoomAsync(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                return Task.FromResult<IReadOnlyList<UserViewModel>>(Array.Empty<UserViewModel>());

            var users = _users.Values
                .Where(u => u.CurrentRoom == roomName)
                .ToList();

            return Task.FromResult<IReadOnlyList<UserViewModel>>(users);
        }

        public Task<IReadOnlyList<UserViewModel>> GetAllUsersAsync()
        {
            var users = _users.Values.ToList();
            return Task.FromResult<IReadOnlyList<UserViewModel>>(users);
        }

        public Task UpdateConnectionIdAsync(string userName, string connectionId)
        {
            if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(connectionId))
            {
                _connectionMap[userName] = connectionId;
            }
            return Task.CompletedTask;
        }

        public Task<string> GetConnectionIdAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return Task.FromResult<string>(null);

            _connectionMap.TryGetValue(userName, out var connectionId);
            return Task.FromResult(connectionId);
        }
    }
}
