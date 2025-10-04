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
        public IEnumerable<ApplicationUser> GetAll() => _users.Values;
        public ApplicationUser GetByUserName(string userName) => _users.GetOrAdd(userName, u => new ApplicationUser { UserName = u, FullName = u });
        public void Upsert(ApplicationUser user) => _users[user.UserName] = user;
    }

    public class InMemoryRoomsRepository : IRoomsRepository
    {
        private readonly ConcurrentDictionary<int, Room> _roomsById = new();
        private readonly ConcurrentDictionary<string, Room> _roomsByName = new();
        private int _id = 0;
        public Room Create(Room room)
        {
            room.Id = System.Threading.Interlocked.Increment(ref _id);
            _roomsById[room.Id] = room;
            _roomsByName[room.Name] = room;
            return room;
        }
        public void Delete(int id)
        {
            if (_roomsById.TryRemove(id, out var r) && r != null)
            {
                _roomsByName.TryRemove(r.Name, out _);
            }
        }
        public IEnumerable<Room> GetAll() => _roomsById.Values;
        public Room GetById(int id) => _roomsById.TryGetValue(id, out var r) ? r : null;
        public Room GetByName(string name) => _roomsByName.TryGetValue(name, out var r) ? r : null;
        public void Update(Room room)
        {
            _roomsById[room.Id] = room;
            _roomsByName[room.Name] = room;
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
    }

    public class InMemoryOtpStore : IOtpStore
    {
        private readonly ConcurrentDictionary<string, (string Code, DateTime Exp)> _codes = new();
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
    }
}
