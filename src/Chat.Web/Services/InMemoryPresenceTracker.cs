using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.ViewModels;

namespace Chat.Web.Services
{
    /// <summary>
    /// In-memory presence tracker for test/dev environments (not suitable for multi-instance).
    /// </summary>
    public class InMemoryPresenceTracker : IPresenceTracker
    {
        private readonly ConcurrentDictionary<string, UserViewModel> _users = new();

        public Task SetUserRoomAsync(string userName, string fullName, string avatar, string roomName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return Task.CompletedTask;

            _users[userName] = new UserViewModel
            {
                UserName = userName,
                FullName = fullName ?? userName,
                Avatar = avatar,
                CurrentRoom = roomName ?? string.Empty
            };

            return Task.CompletedTask;
        }

        public Task<UserViewModel> GetUserAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return Task.FromResult<UserViewModel>(null);

            _users.TryGetValue(userName, out var user);
            return Task.FromResult(user);
        }

        public Task RemoveUserAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return Task.CompletedTask;

            _users.TryRemove(userName, out _);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserViewModel>> GetAllUsersAsync()
        {
            return Task.FromResult<IReadOnlyList<UserViewModel>>(_users.Values.ToList());
        }
    }
}
