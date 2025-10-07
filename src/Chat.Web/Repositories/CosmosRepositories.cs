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

namespace Chat.Web.Repositories
{
    internal static class LogSanitizer
    {
        // Remove characters that could forge new log lines or control terminal output.
        // Limits length to a reasonable size to avoid log spam amplification.
        public static string Sanitize(string input, int max = 200)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
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
            // If TTL was added after existing container creation, update it
            if (defaultTtlSeconds.HasValue && response.Container.ReadContainerAsync().GetAwaiter().GetResult().Resource.DefaultTimeToLive != defaultTtlSeconds.Value)
            {
                var existing = response.Container.ReadContainerAsync().GetAwaiter().GetResult().Resource;
                existing.DefaultTimeToLive = defaultTtlSeconds.Value;
                response.Container.ReplaceContainerAsync(existing).GetAwaiter().GetResult();
            }
            return response.Container;
        }
    }

    // Simple DTOs for Cosmos storage
    internal class UserDoc { public string id { get; set; } public string userName { get; set; } public string fullName { get; set; } public string avatar { get; set; } public string email { get; set; } public string mobile { get; set; } public string[] fixedRooms { get; set; } public string defaultRoom { get; set; } }
    internal class RoomDoc { public string id { get; set; } public string name { get; set; } public string admin { get; set; } }
    internal class MessageDoc { public string id { get; set; } public string roomName { get; set; } public string content { get; set; } public string fromUser { get; set; } public DateTime timestamp { get; set; } }

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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection { {"db.page.count", page.Count} }));
                    list.AddRange(page.Select(d => new ApplicationUser { UserName = d.userName, FullName = d.fullName, Avatar = d.avatar, Email = d.email, MobileNumber = d.mobile, FixedRooms = d.fixedRooms != null ? new System.Collections.Generic.List<string>(d.fixedRooms) : new System.Collections.Generic.List<string>(), DefaultRoom = d.defaultRoom }));
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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null)
                    {
                        return new ApplicationUser { UserName = d.userName, FullName = d.fullName, Avatar = d.avatar, Email = d.email, MobileNumber = d.mobile, FixedRooms = d.fixedRooms != null ? new System.Collections.Generic.List<string>(d.fixedRooms) : new System.Collections.Generic.List<string>(), DefaultRoom = d.defaultRoom };
                    }
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                // Delimit user-controlled value explicitly to avoid ambiguity in logs
                _logger.LogError(ex, "Cosmos user lookup failed User=\"{User}\"", LogSanitizer.Sanitize(userName));
                throw;
            }
        }

        public void Upsert(ApplicationUser user)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.upsert", ActivityKind.Client);
            activity?.SetTag("app.userName", user.UserName);
            var doc = new UserDoc { id = user.UserName, userName = user.UserName, fullName = user.FullName, avatar = user.Avatar, email = user.Email, mobile = user.MobileNumber, fixedRooms = user.FixedRooms != null ? System.Linq.Enumerable.ToArray(user.FixedRooms) : null, defaultRoom = user.DefaultRoom };
            try
            {
                var resp = _users.UpsertItemAsync(doc, new PartitionKey(doc.userName)).GetAwaiter().GetResult();
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

        // Create/Delete removed (static predefined rooms). Upsert happens via Update when seeding.

        public IEnumerable<Room> GetAll()
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getall", ActivityKind.Client);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c ORDER BY c.name"));
            var list = new List<Room>();
            try
            {
                while (q.HasMoreResults)
                {
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d => new Room { Id = int.Parse(d.id), Name = d.name, Admin = new ApplicationUser { UserName = d.admin } }));
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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null) return new Room { Id = int.Parse(d.id), Name = d.name, Admin = new ApplicationUser { UserName = d.admin } };
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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null) return new Room { Id = int.Parse(d.id), Name = d.name, Admin = new ApplicationUser { UserName = d.admin } };
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos room get by name failed {Room}", LogSanitizer.Sanitize(name));
                throw;
            }
        }

        // Update removed (static rooms seeded externally if needed)
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
            var doc = new MessageDoc { id = message.Id.ToString(), roomName = pk, content = message.Content, fromUser = message.FromUser?.UserName, timestamp = message.Timestamp };
            try
            {
                var resp = _messages.UpsertItemAsync(doc, new PartitionKey(pk)).GetAwaiter().GetResult();
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
                    var resp = _messages.DeleteItemAsync<MessageDoc>(id.ToString(), new PartitionKey(pk)).GetAwaiter().GetResult();
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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    var d = page.FirstOrDefault();
                    if (d != null)
                    {
                        return new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, ToRoomId = 0, FromUser = new ApplicationUser { UserName = d.fromUser } };
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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d => new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, FromUser = new ApplicationUser { UserName = d.fromUser } }));
                }
                list = list.OrderBy(m => m.Timestamp).ToList();
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos recent messages failed {Room}", LogSanitizer.Sanitize(roomName));
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
                    var page = q.ReadNextAsync().GetAwaiter().GetResult();
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection {{"db.page.count", page.Count}}));
                    list.AddRange(page.Select(d => new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, FromUser = new ApplicationUser { UserName = d.fromUser } }));
                }
                list = list.OrderBy(m => m.Timestamp).ToList();
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos messages before failed {Room}", LogSanitizer.Sanitize(roomName));
                throw;
            }
        }
    }
}
