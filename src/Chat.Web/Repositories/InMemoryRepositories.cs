using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Chat.Web.Models;
using Chat.Web.Services;
using Chat.Web.Utilities;
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
        public Task<IEnumerable<ApplicationUser>> GetAllAsync() => Task.FromResult<IEnumerable<ApplicationUser>>(_users.Values);
        public Task<ApplicationUser> GetByUserNameAsync(string userName) => Task.FromResult(_users.TryGetValue(userName, out var user) ? user : null);
        public Task<ApplicationUser> GetByUpnAsync(string upn) => Task.FromResult(_users.Values.FirstOrDefault(u => u.Upn == upn));
        public Task UpsertAsync(ApplicationUser user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.UserName)) return Task.CompletedTask;
            // Do not attempt to sync room membership here; Chat.Web treats room users list as externally managed.
            if (_users.TryGetValue(user.UserName, out var existing))
            {
                user.PreferredLanguage = PreferredLanguageMerger.Merge(user.PreferredLanguage, existing.PreferredLanguage);
            }

            _users[user.UserName] = user;
            return Task.CompletedTask;
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
            int id=1; foreach(var r in rooms){ var room=new Room{Id=id++, Name=r, Users = new List<string>(), Languages = new List<string>()}; _roomsById[room.Id]=room; _roomsByName[room.Name]=room; }
        }
        public Task<IEnumerable<Room>> GetAllAsync() => Task.FromResult<IEnumerable<Room>>(_roomsById.Values);
        public Task<Room> GetByIdAsync(int id) => Task.FromResult(_roomsById.TryGetValue(id, out var r) ? r : null);
        public Task<Room> GetByNameAsync(string name) => Task.FromResult(_roomsByName.TryGetValue(name, out var r) ? r : null);
        public Task AddUserToRoomAsync(string roomName, string userName)
        {
            if (_roomsByName.TryGetValue(roomName, out var room))
            {
                var set = new HashSet<string>(room.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                if (set.Add(userName)) room.Users = set.ToList();
            }
            return Task.CompletedTask;
        }
        public Task RemoveUserFromRoomAsync(string roomName, string userName)
        {
            if (_roomsByName.TryGetValue(roomName, out var room) && room.Users != null)
            {
                var set = new HashSet<string>(room.Users, StringComparer.OrdinalIgnoreCase);
                if (set.Remove(userName)) room.Users = set.ToList();
            }
            return Task.CompletedTask;
        }

        public Task AddLanguageToRoomAsync(string roomName, string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return Task.CompletedTask;

            if (_roomsByName.TryGetValue(roomName, out var room))
            {
                var set = new HashSet<string>(room.Languages ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                if (set.Add(language)) room.Languages = set.ToList();
            }
            return Task.CompletedTask;
        }
    }

    public class InMemoryMessagesRepository : IMessagesRepository
    {
        private readonly ConcurrentDictionary<int, Message> _messages = new();
        private int _id = 0;
        public Task<Message> CreateAsync(Message message)
        {
            message.Id = System.Threading.Interlocked.Increment(ref _id);
            _messages[message.Id] = message;
            return Task.FromResult(message);
        }
        public Task DeleteAsync(int id, string byUserName)
        {
            _messages.TryRemove(id, out _);
            return Task.CompletedTask;
        }
        public Task<Message> GetByIdAsync(int id) => Task.FromResult(_messages.TryGetValue(id, out var m) ? m : null);
        public Task<IEnumerable<Message>> GetBeforeByRoomAsync(string roomName, DateTime before, int take = 20) => GetRecentByRoomAsync(roomName, take);
        public Task<IEnumerable<Message>> GetRecentByRoomAsync(string roomName, int take = 20) => Task.FromResult<IEnumerable<Message>>(_messages.Values.Where(m => m.ToRoom != null && m.ToRoom.Name == roomName).OrderByDescending(m => m.Timestamp).Take(take));
        public Task<Message> MarkReadAsync(int id, string userName)
        {
            if (!_messages.TryGetValue(id, out var m) || string.IsNullOrWhiteSpace(userName)) return Task.FromResult<Message>(null);
            var set = new HashSet<string>(m.ReadBy ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (set.Add(userName)) m.ReadBy = set.ToList();
            return Task.FromResult(m);
        }
        
        public Task<Message> UpdateTranslationAsync(
            int id,
            TranslationStatus status,
            System.Collections.Generic.Dictionary<string, string> translations,
            string jobId = null,
            DateTime? failedAt = null,
            TranslationFailureCategory? failureCategory = null,
            TranslationFailureCode? failureCode = null,
            string failureMessage = null)
        {
            if (!_messages.TryGetValue(id, out var m)) return Task.FromResult<Message>(null);
            m.TranslationStatus = status;
            m.Translations = translations ?? new System.Collections.Generic.Dictionary<string, string>();
            m.TranslationJobId = jobId;
            m.TranslationFailedAt = failedAt;

            if (status == TranslationStatus.Failed)
            {
                m.TranslationFailureCategory = failureCategory ?? TranslationFailureCategory.Unknown;
                m.TranslationFailureCode = failureCode ?? TranslationFailureCode.Unknown;
                m.TranslationFailureMessage = failureMessage;
            }
            else
            {
                m.TranslationFailureCategory = TranslationFailureCategory.Unknown;
                m.TranslationFailureCode = TranslationFailureCode.Unknown;
                m.TranslationFailureMessage = null;
            }
            return Task.FromResult(m);
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
