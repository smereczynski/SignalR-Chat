using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Chat.Web.Models;
using Chat.Web.Services;
using System.Threading.Tasks;

namespace Chat.Web.Repositories
{
    public class InMemoryUsersRepository : IUsersRepository
    {
        private readonly ConcurrentDictionary<string, ApplicationUser> _users = new();
        private readonly IRoomsRepository _rooms;
        public InMemoryUsersRepository() { }
        public InMemoryUsersRepository(IRoomsRepository rooms)
        {
            _rooms = rooms;
        }
        public IEnumerable<ApplicationUser> GetAll() => _users.Values;
        public ApplicationUser GetByUserName(string userName) => _users.TryGetValue(userName, out var user) ? user : null;
        public void Upsert(ApplicationUser user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.UserName)) return;
            // Do not attempt to sync room membership here; Chat.Web treats room users list as externally managed.
            _users[user.UserName] = user;
        }
    }

    public class InMemoryRoomsRepository : IRoomsRepository
    {
        private readonly ConcurrentDictionary<int, Room> _roomsById = new();
        private readonly ConcurrentDictionary<string, Room> _roomsByName = new();
        public InMemoryRoomsRepository()
        {
            // Pre-initialize static rooms for testing (deterministic IDs 1..n)
            var rooms = new[]{"general","ops","random"};
            int id=1; foreach(var r in rooms){ var room=new Room{Id=id++, Name=r, Users = new List<string>()}; _roomsById[room.Id]=room; _roomsByName[room.Name]=room; }
        }
        public IEnumerable<Room> GetAll() => _roomsById.Values;
        public Room GetById(int id) => _roomsById.TryGetValue(id, out var r) ? r : null;
        public Room GetByName(string name) => _roomsByName.TryGetValue(name, out var r) ? r : null;
        public void AddUserToRoom(string roomName, string userName)
        {
            if (_roomsByName.TryGetValue(roomName, out var room))
            {
                var set = new HashSet<string>(room.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                if (set.Add(userName)) room.Users = set.ToList();
            }
        }
        public void RemoveUserFromRoom(string roomName, string userName)
        {
            if (_roomsByName.TryGetValue(roomName, out var room) && room.Users != null)
            {
                var set = new HashSet<string>(room.Users, StringComparer.OrdinalIgnoreCase);
                if (set.Remove(userName)) room.Users = set.ToList();
            }
        }
    }

    public class InMemoryMessagesRepository : IMessagesRepository
    {
        private readonly ConcurrentDictionary<int, Message> _messages = new();
        private int _id = 0;
        public Message Create(Message message)
        {
            message.Id = System.Threading.Interlocked.Increment(ref _id);
            _messages[message.Id] = message;
            return message;
        }
        public void Delete(int id, string byUserName) => _messages.TryRemove(id, out _);
        public Message GetById(int id) => _messages.TryGetValue(id, out var m) ? m : null;
        public IEnumerable<Message> GetBeforeByRoom(string roomName, DateTime before, int take = 20) => GetRecentByRoom(roomName, take);
        public IEnumerable<Message> GetRecentByRoom(string roomName, int take = 20) => _messages.Values.Where(m => m.ToRoom != null && m.ToRoom.Name == roomName).OrderByDescending(m => m.Timestamp).Take(take);
        public Message MarkRead(int id, string userName)
        {
            if (!_messages.TryGetValue(id, out var m) || string.IsNullOrWhiteSpace(userName)) return null;
            var set = new HashSet<string>(m.ReadBy ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (set.Add(userName)) m.ReadBy = set.ToList();
            return m;
        }
    }

    public class InMemoryOtpStore : IOtpStore
    {
        private readonly ConcurrentDictionary<string, (string Code, DateTime Exp)> _codes = new();
        private readonly ConcurrentDictionary<string, (int Count, DateTime Exp)> _attempts = new();
        
        public Task SetAsync(string userName, string code, TimeSpan ttl)
        {
            _codes[userName] = (code, DateTime.UtcNow.Add(ttl));
            return Task.CompletedTask;
        }
        public Task<string> GetAsync(string userName)
        {
            if (_codes.TryGetValue(userName, out var entry) && entry.Exp > DateTime.UtcNow)
            {
                return Task.FromResult(entry.Code);
            }
            return Task.FromResult<string>(null);
        }
        public Task RemoveAsync(string userName)
        {
            _codes.TryRemove(userName, out _);
            return Task.CompletedTask;
        }
        
        public Task<int> IncrementAttemptsAsync(string userName, TimeSpan ttl)
        {
            var expiry = DateTime.UtcNow.Add(ttl);
            var count = _attempts.AddOrUpdate(
                userName,
                _ => (1, expiry),
                (_, existing) => existing.Exp > DateTime.UtcNow 
                    ? (existing.Count + 1, existing.Exp) 
                    : (1, expiry)
            ).Count;
            return Task.FromResult(count);
        }
        
        public Task<int> GetAttemptsAsync(string userName)
        {
            if (_attempts.TryGetValue(userName, out var entry) && entry.Exp > DateTime.UtcNow)
            {
                return Task.FromResult(entry.Count);
            }
            return Task.FromResult(0);
        }
    }
}
