using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Chat.Web.Models;
using Chat.Web.Options;
using System.Diagnostics;
using Chat.Web.Observability;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Sockets;
using Chat.Web.Resilience;

namespace Chat.Web.Repositories
{
    internal static class LogSanitizer
    {
        // Remove characters that could forge new log lines or control terminal output.
        // Limits length to a reasonable size to avoid log spam amplification.
        public static string Sanitize(string input, int max = 200)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (ch == '\r' || ch == '\n') continue; // drop new lines entirely
                if (char.IsControl(ch)) continue; // remove other control chars (tabs, etc.)
                sb.Append(ch);
                if (sb.Length >= max)
                {
                    sb.Append("…");
                    break;
                }
            }
            return sb.ToString();
        }

        // Mask an email address, preserving only the domain suffix for minimal utility.
        // Example: john.doe@example.com -> ***@***.com
        public static string MaskEmail(string email)
        {
            var s = Sanitize(email, max: 256);
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var at = s.IndexOf('@');
            if (at < 0) return MaskGeneric(s);
            var lastDot = s.LastIndexOf('.');
            var suffix = lastDot > at && lastDot < s.Length - 1 ? s.Substring(lastDot) : string.Empty;
            return $"***@***{suffix}";
        }

        // Mask a phone number keeping leading '+' (if present) and last 2 digits.
        // Non-digit characters (spaces/dashes) are removed in the mask.
        // Example: +48604970937 -> +*********37
        public static string MaskPhone(string phone)
        {
            var s = Sanitize(phone, max: 64);
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var hasPlus = s.StartsWith('+');
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return hasPlus ? "+**" : "**";
            var keep = Math.Min(2, digits.Length);
            var stars = new string('*', Math.Max(0, digits.Length - keep));
            var tail = digits.Substring(digits.Length - keep, keep);
            return (hasPlus ? "+" : string.Empty) + stars + tail;
        }

        // Heuristic mask for destinations (email or phone or other handle)
        public static string MaskDestination(string dest)
        {
            if (string.IsNullOrWhiteSpace(dest)) return string.Empty;
            var s = Sanitize(dest, max: 256);
            if (s.Contains('@')) return MaskEmail(s);
            // Assume phone-like if it contains 5+ digits
            var digitCount = s.Count(char.IsDigit);
            if (digitCount >= 5) return MaskPhone(s);
            return MaskGeneric(s);
        }

        private static string MaskGeneric(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var keep = Math.Min(2, s.Length);
            var tail = s.Substring(s.Length - keep, keep);
            return new string('*', Math.Max(0, s.Length - keep)) + tail;
        }
    }
    public class CosmosClients
    {
        public CosmosClient Client { get; }
        public Database Database { get; }
        public Container Users { get; }
        public Container Rooms { get; }
        public Container Messages { get; }

        public CosmosClients(CosmosOptions options)
        {
            Client = new CosmosClient(options.ConnectionString);
            if (options.AutoCreate)
            {
                Database = Client.CreateDatabaseIfNotExistsAsync(options.Database).GetAwaiter().GetResult();
                Users = CreateContainerIfNotExists(options.UsersContainer, "/userName", 400);
                Rooms = CreateContainerIfNotExists(options.RoomsContainer, "/name", 400);
                Messages = CreateContainerIfNotExists(options.MessagesContainer, "/roomName", 400, options.MessagesTtlSeconds);
            }
            else
            {
                Database = Client.GetDatabase(options.Database);
                Users = Database.GetContainer(options.UsersContainer);
                Rooms = Database.GetContainer(options.RoomsContainer);
                Messages = Database.GetContainer(options.MessagesContainer);
            }
        }

        private Container CreateContainerIfNotExists(string name, string partitionKey, int? throughput, int? defaultTtlSeconds = null)
        {
            var props = new ContainerProperties(name, partitionKey);
            if (defaultTtlSeconds.HasValue)
            {
                props.DefaultTimeToLive = defaultTtlSeconds.Value;
            }
            var response = Database.CreateContainerIfNotExistsAsync(props, throughput).GetAwaiter().GetResult();
            // Reconcile TTL setting on existing container
            var current = response.Container.ReadContainerAsync().GetAwaiter().GetResult().Resource;
            if (defaultTtlSeconds.HasValue)
            {
                // If TTL should be a specific value and differs, update it
                if (current.DefaultTimeToLive != defaultTtlSeconds.Value)
                {
                    current.DefaultTimeToLive = defaultTtlSeconds.Value;
                    response.Container.ReplaceContainerAsync(current).GetAwaiter().GetResult();
                }
            }
            else
            {
                // TTL should be disabled entirely (null). If currently set, clear it.
                if (current.DefaultTimeToLive != null)
                {
                    current.DefaultTimeToLive = null;
                    response.Container.ReplaceContainerAsync(current).GetAwaiter().GetResult();
                }
            }
            return response.Container;
        }
    }

    // Simple DTOs for Cosmos storage
    internal class UserDoc { public string id { get; set; } public string userName { get; set; } public string fullName { get; set; } public string avatar { get; set; } public string email { get; set; } public string mobile { get; set; } public bool? enabled { get; set; } public string[] fixedRooms { get; set; } public string[] rooms { get; set; } public string defaultRoom { get; set; } }
    internal class RoomDoc { public string id { get; set; } public string name { get; set; } public string admin { get; set; } public string[] users { get; set; } }
    internal class MessageDoc { public string id { get; set; } public string roomName { get; set; } public string content { get; set; } public string fromUser { get; set; } public DateTime timestamp { get; set; } public string[] readBy { get; set; } }

    public class CosmosUsersRepository : IUsersRepository
    {
        private readonly Container _users;
        private readonly ILogger<CosmosUsersRepository> _logger;
        public CosmosUsersRepository(CosmosClients clients, ILogger<CosmosUsersRepository> logger)
        {
            _users = clients.Users;
            _logger = logger;
        }

        public IEnumerable<ApplicationUser> GetAll()
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.getall", ActivityKind.Client);
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT * FROM c"));
            var list = new List<ApplicationUser>();
            try
            {
                while (q.HasMoreResults)
                {
                    // retry each page fetch
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.users.getall.readnext"
                    ).GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d =>
                    {
                        var rooms = d.fixedRooms ?? d.rooms; // support Admin schema (rooms) and legacy (fixedRooms)
                        var fixedRooms = rooms != null ? new System.Collections.Generic.List<string>(rooms) : new System.Collections.Generic.List<string>();
                        // Default room: prefer explicit defaultRoom; otherwise first allowed room if available
                        var def = !string.IsNullOrWhiteSpace(d.defaultRoom) ? d.defaultRoom : (fixedRooms.Count > 0 ? fixedRooms[0] : null);
                        return new ApplicationUser
                        {
                            UserName = d.userName,
                            FullName = d.fullName,
                            Avatar = d.avatar,
                            Email = d.email,
                            MobileNumber = d.mobile,
                            Enabled = d.enabled ?? true,
                            FixedRooms = fixedRooms,
                            DefaultRoom = def
                        };
                    }));
                }
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos users get all failed");
                throw;
            }
        }

        public ApplicationUser GetByUserName(string userName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.get", ActivityKind.Client);
            activity?.SetTag("app.userName", userName);
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT * FROM c WHERE c.userName = @u").WithParameter("@u", userName));
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.users.get.readnext").GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null)
                    {
                        var rooms = d.fixedRooms ?? d.rooms; // support Admin schema
                        var fixedRooms = rooms != null ? new System.Collections.Generic.List<string>(rooms) : new System.Collections.Generic.List<string>();
                        var def = !string.IsNullOrWhiteSpace(d.defaultRoom) ? d.defaultRoom : (fixedRooms.Count > 0 ? fixedRooms[0] : null);
                        return new ApplicationUser { UserName = d.userName, FullName = d.fullName, Avatar = d.avatar, Email = d.email, MobileNumber = d.mobile, Enabled = d.enabled ?? true, FixedRooms = fixedRooms, DefaultRoom = def };
                    }
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Removed user-supplied value from log to avoid any disclosure/log forging risk
                _logger.LogError(ex, "Cosmos user lookup failed");
                throw;
            }
        }

        public void Upsert(ApplicationUser user)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.upsert", ActivityKind.Client);
            activity?.SetTag("app.userName", user.UserName);
            var doc = new UserDoc { id = user.UserName, userName = user.UserName, fullName = user.FullName, avatar = user.Avatar, email = user.Email, mobile = user.MobileNumber, enabled = user.Enabled, fixedRooms = user.FixedRooms != null ? System.Linq.Enumerable.ToArray(user.FixedRooms) : null, rooms = user.FixedRooms != null ? System.Linq.Enumerable.ToArray(user.FixedRooms) : null, defaultRoom = user.DefaultRoom };
            try
            {
                var resp = Resilience.RetryHelper.ExecuteAsync(
                    _ => _users.UpsertItemAsync(doc, new PartitionKey(doc.userName)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.users.upsert").GetAwaiter().GetResult();
                activity?.SetTag("db.status_code", (int)resp.StatusCode);
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos user upsert failed {User}", LogSanitizer.Sanitize(user.UserName));
                throw;
            }
        }
    }

    public class CosmosRoomsRepository : IRoomsRepository
    {
        private readonly Container _rooms;
        private readonly ILogger<CosmosRoomsRepository> _logger;

        public CosmosRoomsRepository(CosmosClients clients, ILogger<CosmosRoomsRepository> logger)
        {
            _rooms = clients.Rooms;
            _logger = logger;
        }

        public IEnumerable<Room> GetAll()
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getall", ActivityKind.Client);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c ORDER BY c.name"));
            var list = new List<Room>();
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.rooms.getall.readnext").GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d => new Room { Id = DocIdUtil.TryParseRoomId(d.id), Name = d.name, Users = d.users != null ? new List<string>(d.users) : new List<string>() }));
                }
                list = list.OrderBy(r => r.Name).ToList();
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos rooms get all failed");
                throw;
            }
        }

        public Room GetById(int id)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getbyid", ActivityKind.Client);
            activity?.SetTag("app.room.id", id);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.rooms.getbyid.readnext").GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null) return new Room { Id = DocIdUtil.TryParseRoomId(d.id), Name = d.name, Users = d.users != null ? new List<string>(d.users) : new List<string>() };
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos room get by id failed {Id}", id);
                throw;
            }
        }

        public Room GetByName(string name)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getbyname", ActivityKind.Client);
            activity?.SetTag("app.room", name);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.name = @n").WithParameter("@n", name));
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.rooms.getbyname.readnext").GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null) return new Room { Id = DocIdUtil.TryParseRoomId(d.id), Name = d.name, Users = d.users != null ? new List<string>(d.users) : new List<string>() };
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Removed user-supplied room name from log to avoid disclosure/log forging risk
                _logger.LogError(ex, "Cosmos room get by name failed");
                throw;
            }
        }

        public void AddUserToRoom(string roomName, string userName)
        {
            UpsertRoomUser(roomName, userName, add: true);
        }

        public void RemoveUserFromRoom(string roomName, string userName)
        {
            UpsertRoomUser(roomName, userName, add: false);
        }

        private void UpsertRoomUser(string roomName, string userName, bool add)
        {
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.name = @n").WithParameter("@n", roomName));
            RoomDoc room = null;
            while (q.HasMoreResults && room == null)
            {
                var page = Resilience.RetryHelper.ExecuteAsync(
                    _ => q.ReadNextAsync(),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.rooms.byname.forRoomRepo").GetAwaiter().GetResult();
                room = page.FirstOrDefault();
            }
            if (room == null) return;
            var users = new HashSet<string>(room.users ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (add ? users.Add(userName) : users.Remove(userName))
            {
                room.users = users.ToArray();
                _rooms.UpsertItemAsync(room, new PartitionKey(roomName)).GetAwaiter().GetResult();
            }
        }
    }

    internal static class DocIdUtil
    {
        public static int TryParseRoomId(string id)
        {
            if (int.TryParse(id, out var v)) return v;
            // Derive a stable positive 32-bit int from the string id
            unchecked
            {
                int hash = 23;
                foreach (var ch in id ?? string.Empty)
                    hash = hash * 31 + ch;
                return hash == int.MinValue ? int.MaxValue : Math.Abs(hash);
            }
        }
    }

    public class CosmosMessagesRepository : IMessagesRepository
    {
        private readonly Container _messages;
        private readonly IRoomsRepository _roomsRepo;
        private readonly ILogger<CosmosMessagesRepository> _logger;

        public CosmosMessagesRepository(CosmosClients clients, IRoomsRepository roomsRepo, ILogger<CosmosMessagesRepository> logger)
        {
            _messages = clients.Messages;
            _roomsRepo = roomsRepo;
            _logger = logger;
        }

        public Message Create(Message message)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.create", ActivityKind.Client);
            var room = message.ToRoom ?? _roomsRepo.GetById(message.ToRoomId);
            var pk = room?.Name ?? "global";
            message.Id = message.Id == 0 ? new Random().Next(1, int.MaxValue) : message.Id;
            var doc = new MessageDoc { id = message.Id.ToString(), roomName = pk, content = message.Content, fromUser = message.FromUser?.UserName, timestamp = message.Timestamp, readBy = (message.ReadBy != null ? message.ReadBy.ToArray() : Array.Empty<string>()) };
            try
            {
                var resp = Resilience.RetryHelper.ExecuteAsync(
                    _ => _messages.UpsertItemAsync(doc, new PartitionKey(pk)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.create").GetAwaiter().GetResult();
                activity?.SetTag("db.status_code", (int)resp.StatusCode);
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos message create failed {Room}", LogSanitizer.Sanitize(pk));
                throw;
            }
            return message;
        }

        public void Delete(int id, string byUserName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.delete", ActivityKind.Client);
            var m = GetById(id);
            if (m?.FromUser?.UserName == byUserName)
            {
                var room = m.ToRoom ?? _roomsRepo.GetById(m.ToRoomId);
                var pk = room?.Name ?? "global";
                try
                {
                    var resp = Resilience.RetryHelper.ExecuteAsync(
                        _ => _messages.DeleteItemAsync<MessageDoc>(id.ToString(), new PartitionKey(pk)),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.messages.delete").GetAwaiter().GetResult();
                    activity?.SetTag("db.status_code", (int)resp.StatusCode);
                }
                catch (CosmosException ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    _logger.LogError(ex, "Cosmos message delete failed {Id}", id); // id is integer; no sanitization needed
                    throw;
                }
            }
        }

        public Message GetById(int id)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.getbyid", ActivityKind.Client);
            activity?.SetTag("app.message.id", id);
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.messages.getbyid.readnext").GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null)
                    {
                        return new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, ToRoomId = 0, FromUser = new ApplicationUser { UserName = d.fromUser }, ReadBy = d.readBy != null ? new List<string>(d.readBy) : new List<string>() };
                    }
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos message get by id failed {Id}", id);
                throw;
            }
        }

        public IEnumerable<Message> GetRecentByRoom(string roomName, int take = 20)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.recent", ActivityKind.Client);
            activity?.SetTag("app.room", roomName);
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition($"SELECT TOP {take} * FROM c WHERE c.roomName = @n ORDER BY c.timestamp DESC").WithParameter("@n", roomName), requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roomName) });
            var list = new List<Message>();
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.messages.recent.readnext").GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d => new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, FromUser = new ApplicationUser { UserName = d.fromUser }, ReadBy = d.readBy != null ? new List<string>(d.readBy) : new List<string>() }));
                }
                list = list.OrderBy(m => m.Timestamp).ToList();
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Removed user-supplied room name from log
                _logger.LogError(ex, "Cosmos recent messages failed");
                throw;
            }
        }

    public IEnumerable<Message> GetBeforeByRoom(string roomName, DateTime before, int take = 20)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.before", ActivityKind.Client);
            activity?.SetTag("app.room", roomName);
            activity?.SetTag("app.before", before);
            // Query older messages strictly before provided timestamp
            var q = _messages.GetItemQueryIterator<MessageDoc>(
                new QueryDefinition($"SELECT TOP {take} * FROM c WHERE c.roomName = @n AND c.timestamp < @b ORDER BY c.timestamp DESC")
                    .WithParameter("@n", roomName)
                    .WithParameter("@b", before),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roomName) });
            var list = new List<Message>();
            try
            {
                while (q.HasMoreResults)
                {
                    var page = Resilience.RetryHelper.ExecuteAsync(
                        _ => q.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.messages.before.readnext").GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d => new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, FromUser = new ApplicationUser { UserName = d.fromUser }, ReadBy = d.readBy != null ? new List<string>(d.readBy) : new List<string>() }));
                }
                // Keep only the newest 'take' items and return ascending order
                list = list.OrderByDescending(m => m.Timestamp).Take(take).OrderBy(m => m.Timestamp).ToList();
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Removed user-supplied room name from log
                _logger.LogError(ex, "Cosmos messages before failed");
                throw;
            }
        }

        public Message MarkRead(int id, string userName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.markread", ActivityKind.Client);
            activity?.SetTag("app.message.id", id);
            if (string.IsNullOrWhiteSpace(userName)) return null;
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            MessageDoc d = null;
            while (q.HasMoreResults && d == null)
            {
                var page = Resilience.RetryHelper.ExecuteAsync(
                    _ => q.ReadNextAsync(),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.markread.lookup").GetAwaiter().GetResult();
                d = page.FirstOrDefault();
            }
            if (d == null) return null;
            var pk = d.roomName;
            var set = new HashSet<string>(d.readBy ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (set.Add(userName))
            {
                d.readBy = set.ToArray();
                try
                {
                    var resp = Resilience.RetryHelper.ExecuteAsync(
                        _ => _messages.UpsertItemAsync(d, new PartitionKey(pk)),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.messages.markread.upsert").GetAwaiter().GetResult();
                    activity?.SetTag("db.status_code", (int)resp.StatusCode);
                }
                catch (CosmosException ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    _logger.LogError(ex, "Cosmos message mark read failed {Id}", id);
                    throw;
                }
            }
            return new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, FromUser = new ApplicationUser { UserName = d.fromUser }, ReadBy = d.readBy != null ? new List<string>(d.readBy) : new List<string>() };
        }
    }
}
