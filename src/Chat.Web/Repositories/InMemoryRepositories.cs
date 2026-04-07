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
    public class InMemoryDispatchCentersRepository : IDispatchCentersRepository
    {
        private readonly ConcurrentDictionary<string, DispatchCenter> _dispatchCenters = new(StringComparer.OrdinalIgnoreCase);

        public Task<IEnumerable<DispatchCenter>> GetAllAsync() => Task.FromResult<IEnumerable<DispatchCenter>>(_dispatchCenters.Values);

        public Task<DispatchCenter> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<DispatchCenter>(null);
            return Task.FromResult(_dispatchCenters.TryGetValue(id, out var dc) ? dc : null);
        }

        public Task<DispatchCenter> GetByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Task.FromResult<DispatchCenter>(null);
            var match = _dispatchCenters.Values.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match);
        }

        public Task UpsertAsync(DispatchCenter dispatchCenter)
        {
            if (dispatchCenter == null) return Task.CompletedTask;

            dispatchCenter.Id ??= Guid.NewGuid().ToString();
            dispatchCenter.CorrespondingDispatchCenterIds ??= new List<string>();
            dispatchCenter.Users ??= new List<string>();

            _dispatchCenters[dispatchCenter.Id] = dispatchCenter;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return Task.CompletedTask;
            _dispatchCenters.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task AssignUserAsync(string dispatchCenterId, string userName)
        {
            if (string.IsNullOrWhiteSpace(dispatchCenterId) || string.IsNullOrWhiteSpace(userName)) return Task.CompletedTask;
            if (!_dispatchCenters.TryGetValue(dispatchCenterId, out var dc)) return Task.CompletedTask;

            var set = new HashSet<string>(dc.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (set.Add(userName)) dc.Users = set.ToList();
            return Task.CompletedTask;
        }

        public Task UnassignUserAsync(string dispatchCenterId, string userName)
        {
            if (string.IsNullOrWhiteSpace(dispatchCenterId) || string.IsNullOrWhiteSpace(userName)) return Task.CompletedTask;
            if (!_dispatchCenters.TryGetValue(dispatchCenterId, out var dc)) return Task.CompletedTask;

            var set = new HashSet<string>(dc.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (set.Remove(userName)) dc.Users = set.ToList();
            return Task.CompletedTask;
        }
    }

    public class InMemoryUsersRepository : IUsersRepository
    {
        private readonly ConcurrentDictionary<string, ApplicationUser> _users = new(StringComparer.OrdinalIgnoreCase);
        public InMemoryUsersRepository() { }
        public InMemoryUsersRepository(IRoomsRepository rooms) { }
        public Task<IEnumerable<ApplicationUser>> GetAllAsync() => Task.FromResult<IEnumerable<ApplicationUser>>(_users.Values);
        public Task<IEnumerable<ApplicationUser>> GetByDispatchCenterIdAsync(string dispatchCenterId)
            => Task.FromResult<IEnumerable<ApplicationUser>>(
                _users.Values.Where(u => string.Equals(u.DispatchCenterId, dispatchCenterId, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ApplicationUser> GetByUserNameAsync(string userName) => Task.FromResult(_users.TryGetValue(userName, out var user) ? user : null);
        public Task<ApplicationUser> GetByUpnAsync(string upn) => Task.FromResult(_users.Values.FirstOrDefault(u => string.Equals(u.Upn, upn, StringComparison.OrdinalIgnoreCase)));
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
        private readonly ConcurrentDictionary<string, Room> _roomsByName = new(StringComparer.OrdinalIgnoreCase);
        public InMemoryRoomsRepository() { }
        public Task<IEnumerable<Room>> GetAllAsync() => Task.FromResult<IEnumerable<Room>>(_roomsById.Values);
        public Task<Room> GetByIdAsync(int id) => Task.FromResult(_roomsById.TryGetValue(id, out var r) ? r : null);
        public Task<Room> GetByNameAsync(string name) => Task.FromResult(_roomsByName.TryGetValue(name, out var r) ? r : null);
        public Task<Room> GetByPairKeyAsync(string pairKey)
            => Task.FromResult(_roomsById.Values.FirstOrDefault(r => string.Equals(r.PairKey, pairKey, StringComparison.OrdinalIgnoreCase)));
        public Task<IEnumerable<Room>> GetByDispatchCenterIdAsync(string dispatchCenterId)
            => Task.FromResult<IEnumerable<Room>>(
                _roomsById.Values.Where(r => r.IsActive && DispatchCenterPairing.IncludesDispatchCenter(r, dispatchCenterId)).ToList());
        public Task UpsertAsync(Room room)
        {
            if (room == null) return Task.CompletedTask;
            if (room.Id == 0)
            {
                room.Id = _roomsById.Keys.DefaultIfEmpty(0).Max() + 1;
            }

            _roomsById[room.Id] = room;
            if (!string.IsNullOrWhiteSpace(room.Name))
            {
                _roomsByName[room.Name] = room;
            }

            return Task.CompletedTask;
        }
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
        public Task<Message> MarkReadAsync(int id, string userName, string dispatchCenterId)
        {
            if (!_messages.TryGetValue(id, out var m) || string.IsNullOrWhiteSpace(userName)) return Task.FromResult<Message>(null);
            var set = new HashSet<string>(m.ReadBy ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (set.Add(userName)) m.ReadBy = set.ToList();
            if (!string.IsNullOrWhiteSpace(dispatchCenterId))
            {
                var readByDispatchCenters = new HashSet<string>(m.ReadByDispatchCenterIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                if (readByDispatchCenters.Add(dispatchCenterId)) m.ReadByDispatchCenterIds = readByDispatchCenters.ToList();
            }
            return Task.FromResult(m);
        }
        
        public Task<Message> UpdateTranslationAsync(
            int id,
            MessageTranslationUpdate update)
        {
            if (!_messages.TryGetValue(id, out var m)) return Task.FromResult<Message>(null);
            m.TranslationStatus = update.Status;
            m.Translations = update.Translations ?? new System.Collections.Generic.Dictionary<string, string>();
            m.TranslationJobId = update.JobId;
            m.TranslationFailedAt = update.FailedAt;

            if (update.Status == TranslationStatus.Failed)
            {
                m.TranslationFailureCategory = update.FailureCategory ?? TranslationFailureCategory.Unknown;
                m.TranslationFailureCode = update.FailureCode ?? TranslationFailureCode.Unknown;
                m.TranslationFailureMessage = update.FailureMessage;
            }
            else
            {
                m.TranslationFailureCategory = TranslationFailureCategory.Unknown;
                m.TranslationFailureCode = TranslationFailureCode.Unknown;
                m.TranslationFailureMessage = null;
            }
            return Task.FromResult(m);
        }

        public Task<Message> UpdateEscalationAsync(int id, MessageEscalationStatus status, string escalationId)
        {
            if (!_messages.TryGetValue(id, out var m)) return Task.FromResult<Message>(null);
            m.EscalationStatus = status;
            m.OpenEscalationId = escalationId;
            return Task.FromResult(m);
        }
    }

    public class InMemoryEscalationsRepository : IEscalationsRepository
    {
        private readonly ConcurrentDictionary<string, Escalation> _escalations = new(StringComparer.OrdinalIgnoreCase);

        public Task<Escalation> CreateAsync(Escalation escalation)
        {
            escalation.Id ??= Guid.NewGuid().ToString();
            _escalations[escalation.Id] = escalation;
            return Task.FromResult(escalation);
        }

        public Task<Escalation> GetByIdAsync(string id, string roomName)
        {
            if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Escalation>(null);
            _escalations.TryGetValue(id, out var escalation);
            if (escalation == null) return Task.FromResult<Escalation>(null);
            if (!string.IsNullOrWhiteSpace(roomName) && !string.Equals(escalation.RoomName, roomName, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<Escalation>(null);
            return Task.FromResult(escalation);
        }

        public Task<IEnumerable<Escalation>> GetByRoomAsync(string roomName, int take = 50)
        {
            var items = _escalations.Values
                .Where(x => string.Equals(x.RoomName, roomName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAt)
                .Take(take)
                .ToList();
            return Task.FromResult<IEnumerable<Escalation>>(items);
        }

        public Task<IEnumerable<Escalation>> GetDueScheduledAsync(DateTime dueBeforeUtc, int take = 100)
        {
            var items = _escalations.Values
                .Where(x => x.Status == Models.EscalationStatus.Scheduled && x.DueAt <= dueBeforeUtc)
                .OrderBy(x => x.DueAt)
                .Take(take)
                .ToList();
            return Task.FromResult<IEnumerable<Escalation>>(items);
        }

        public Task<Escalation> GetOpenByMessageIdAsync(int messageId)
        {
            var escalation = _escalations.Values
                .FirstOrDefault(x =>
                    (x.Status == Models.EscalationStatus.Scheduled || x.Status == Models.EscalationStatus.Escalated) &&
                    (x.MessageIds?.Contains(messageId) ?? false));
            return Task.FromResult(escalation);
        }

        public Task UpsertAsync(Escalation escalation)
        {
            if (escalation == null) return Task.CompletedTask;
            escalation.Id ??= Guid.NewGuid().ToString();
            _escalations[escalation.Id] = escalation;
            return Task.CompletedTask;
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
