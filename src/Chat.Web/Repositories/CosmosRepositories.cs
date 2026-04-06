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
using System.Net.Sockets;
using Chat.Web.Resilience;
using Chat.Web.Utilities;

namespace Chat.Web.Repositories
{
    public class CosmosClients
    {
        public CosmosClient Client { get; }
        public Database Database { get; }
        public Container Users { get; }
        public Container Rooms { get; }
        public Container Messages { get; }
        public Container DispatchCenters { get; }
        public Container Escalations { get; }

        private CosmosClients(CosmosClient client, Database database, Container users, Container rooms, Container messages, Container dispatchCenters, Container escalations)
        {
            Client = client;
            Database = database;
            Users = users;
            Rooms = rooms;
            Messages = messages;
            DispatchCenters = dispatchCenters;
            Escalations = escalations;
        }

        public static async Task<CosmosClients> CreateAsync(CosmosOptions options)
        {
            // Use Gateway mode for private endpoint compatibility
            // Gateway mode uses HTTPS and respects DNS resolution for private endpoints
            var clientOptions = new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway
            };
            
            var client = new CosmosClient(options.ConnectionString, clientOptions);
            Database database;
            Container users, rooms, messages, dispatchCenters, escalations;

            if (options.AutoCreate)
            {
                database = await client.CreateDatabaseIfNotExistsAsync(options.Database).ConfigureAwait(false);
                users = await CreateContainerIfNotExistsAsync(database, options.UsersContainer, "/userName", 400).ConfigureAwait(false);
                rooms = await CreateContainerIfNotExistsAsync(database, options.RoomsContainer, "/name", 400).ConfigureAwait(false);
                messages = await CreateContainerIfNotExistsAsync(database, options.MessagesContainer, "/roomName", 400, options.MessagesTtlSeconds).ConfigureAwait(false);
                dispatchCenters = await CreateContainerIfNotExistsAsync(database, options.DispatchCentersContainer, "/id", 400).ConfigureAwait(false);
                escalations = await CreateContainerIfNotExistsAsync(database, options.EscalationsContainer, "/roomName", 400).ConfigureAwait(false);
            }
            else
            {
                database = client.GetDatabase(options.Database);
                users = database.GetContainer(options.UsersContainer);
                rooms = database.GetContainer(options.RoomsContainer);
                messages = database.GetContainer(options.MessagesContainer);
                dispatchCenters = database.GetContainer(options.DispatchCentersContainer);
                escalations = database.GetContainer(options.EscalationsContainer);
            }

            return new CosmosClients(client, database, users, rooms, messages, dispatchCenters, escalations);
        }

        private static async Task<Container> CreateContainerIfNotExistsAsync(Database database, string name, string partitionKey, int? throughput, int? defaultTtlSeconds = null)
        {
            var props = new ContainerProperties(name, partitionKey);
            if (defaultTtlSeconds.HasValue)
            {
                props.DefaultTimeToLive = defaultTtlSeconds.Value;
            }
            var response = await database.CreateContainerIfNotExistsAsync(props, throughput).ConfigureAwait(false);
            // Reconcile TTL setting on existing container
            var current = (await response.Container.ReadContainerAsync().ConfigureAwait(false)).Resource;
            if (defaultTtlSeconds.HasValue)
            {
                // If TTL should be a specific value and differs, update it
                if (current.DefaultTimeToLive != defaultTtlSeconds.Value)
                {
                    current.DefaultTimeToLive = defaultTtlSeconds.Value;
                    await response.Container.ReplaceContainerAsync(current).ConfigureAwait(false);
                }
            }
            else
            {
                // TTL should be disabled entirely (null). If currently set, clear it.
                if (current.DefaultTimeToLive != null)
                {
                    current.DefaultTimeToLive = null;
                    await response.Container.ReplaceContainerAsync(current).ConfigureAwait(false);
                }
            }
            return response.Container;
        }
    }

    // Simple DTOs for Cosmos storage
    internal class UserDoc 
    { 
        public string id { get; set; } 
        public string userName { get; set; } 
        public string fullName { get; set; } 
        public string avatar { get; set; } 
        public string preferredLanguage { get; set; }
        public string email { get; set; } 
        public string mobile { get; set; } 
        public bool? enabled { get; set; } 
        public string upn { get; set; }
        public string tenantId { get; set; }
        public string displayName { get; set; }
        public string country { get; set; }
        public string region { get; set; }
        public string[] fixedRooms { get; set; } 
        public string[] dispatchCenterIds { get; set; }
        public string dispatchCenterId { get; set; }
        public string defaultRoom { get; set; } 
    }
    internal class RoomDoc
    {
        public string id { get; set; }
        public string name { get; set; }
        public string displayName { get; set; }
        public string admin { get; set; }
        public string roomType { get; set; }
        public string pairKey { get; set; }
        public string dispatchCenterAId { get; set; }
        public string dispatchCenterBId { get; set; }
        public bool? isActive { get; set; }
        public string[] users { get; set; }
        public string[] languages { get; set; }
    }
    internal class DispatchCenterDoc
    {
        public string id { get; set; }
        public string name { get; set; }
        public string country { get; set; }
        public bool ifMain { get; set; }
        public string[] correspondingDispatchCenterIds { get; set; }
        public string[] users { get; set; }
        public string officerUserName { get; set; }
    }
    internal class MessageDoc 
    { 
        public string id { get; set; } 
        public string roomName { get; set; } 
        public string content { get; set; } 
        public string fromUser { get; set; } 
        public DateTime timestamp { get; set; } 
        public string[] readBy { get; set; }
        public string translationStatus { get; set; }  // "None", "Pending", "InProgress", "Completed", "Failed"
        public Dictionary<string, string> translations { get; set; }  // {"en": "Hello", "pl": "Cześć"}
        public string translationJobId { get; set; }  // "transjob:123:1638360000000"
        public DateTime? translationFailedAt { get; set; }  // nullable timestamp
        public string translationFailureCategory { get; set; }
        public string translationFailureCode { get; set; }
        public string translationFailureMessage { get; set; }
        public string fromDispatchCenterId { get; set; }
        public string[] readByDispatchCenterIds { get; set; }
        public string escalationStatus { get; set; }
        public string openEscalationId { get; set; }
    }
    internal class EscalationMessageSnapshotDoc
    {
        public int messageId { get; set; }
        public string content { get; set; }
        public DateTime timestamp { get; set; }
        public string fromUserName { get; set; }
        public string fromDispatchCenterId { get; set; }
    }
    internal class EscalationDoc
    {
        public string id { get; set; }
        public string roomName { get; set; }
        public string pairKey { get; set; }
        public string sourceDispatchCenterId { get; set; }
        public string targetDispatchCenterId { get; set; }
        public string targetOfficerUserName { get; set; }
        public string triggerType { get; set; }
        public string status { get; set; }
        public DateTime createdAt { get; set; }
        public DateTime dueAt { get; set; }
        public DateTime? escalatedAt { get; set; }
        public DateTime? resolvedAt { get; set; }
        public DateTime? cancelledAt { get; set; }
        public string createdByUserName { get; set; }
        public int[] messageIds { get; set; }
        public EscalationMessageSnapshotDoc[] messageSnapshots { get; set; }
    }

    /// <summary>
    /// Helper class to reduce duplication in paginated Cosmos query patterns
    /// </summary>
    internal static class CosmosQueryHelper
    {
        /// <summary>
        /// Executes a paginated Cosmos query with retry logic and telemetry
        /// </summary>
        public static async Task<List<T>> ExecutePaginatedQueryAsync<TDoc, T>(
            FeedIterator<TDoc> queryIterator,
            Func<TDoc, T> mapper,
            Activity activity,
            ILogger logger,
            string operationName)
        {
            var list = new List<T>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var page = await Resilience.RetryHelper.ExecuteAsync(
                        _ => queryIterator.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        logger,
                        $"{operationName}.readnext");
                    activity?.AddEvent(new ActivityEvent("page", tags: new ActivityTagsCollection { { "db.page.count", page.Count } }));
                    list.AddRange(page.Select(mapper));
                }
                activity?.SetTag("app.result.count", list.Count);
                return list;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "{Operation} failed", operationName);
                throw;
            }
        }

        /// <summary>
        /// Executes a single-result Cosmos query with retry logic and telemetry
        /// </summary>
        public static async Task<T> ExecuteSingleResultQueryAsync<TDoc, T>(
            FeedIterator<TDoc> queryIterator,
            Func<TDoc, T> mapper,
            Activity activity,
            ILogger logger,
            string operationName) where TDoc : class where T : class
        {
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var page = await Resilience.RetryHelper.ExecuteAsync(
                        _ => queryIterator.ReadNextAsync(),
                        Transient.IsCosmosTransient,
                        logger,
                        $"{operationName}.readnext");
                    var doc = page.FirstOrDefault();
                    if (doc != null)
                    {
                        return mapper(doc);
                    }
                }
                return null;
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "{Operation} failed", operationName);
                throw;
            }
        }
    }

    public class CosmosUsersRepository : IUsersRepository
    {
        private readonly Container _users;
        private readonly ILogger<CosmosUsersRepository> _logger;
        public CosmosUsersRepository(CosmosClients clients, ILogger<CosmosUsersRepository> logger)
        {
            _users = clients.Users;
            _logger = logger;
        }

        private static ApplicationUser MapUser(UserDoc d)
        {
            var rooms = d.fixedRooms;
            var fixedRooms = rooms != null ? new System.Collections.Generic.List<string>(rooms) : new System.Collections.Generic.List<string>();
            var dispatchCenterIds = d.dispatchCenterIds != null
                ? new System.Collections.Generic.List<string>(d.dispatchCenterIds)
                : new System.Collections.Generic.List<string>();
            var def = !string.IsNullOrWhiteSpace(d.defaultRoom) ? d.defaultRoom : (fixedRooms.Count > 0 ? fixedRooms[0] : null);
            return new ApplicationUser
            {
                UserName = d.userName,
                FullName = d.fullName,
                Avatar = d.avatar,
                PreferredLanguage = d.preferredLanguage,
                Email = d.email,
                MobileNumber = d.mobile,
                Enabled = d.enabled ?? true,
                Upn = d.upn,
                TenantId = d.tenantId,
                DisplayName = d.displayName,
                Country = d.country,
                Region = d.region,
                FixedRooms = fixedRooms,
                DispatchCenterIds = dispatchCenterIds,
                DispatchCenterId = d.dispatchCenterId,
                DefaultRoom = def
            };
        }

        public async Task<IEnumerable<ApplicationUser>> GetAllAsync()
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.getall", ActivityKind.Client);
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT * FROM c"));
            return await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapUser, activity, _logger, "cosmos.users.getall").ConfigureAwait(false);
        }

        public async Task<IEnumerable<ApplicationUser>> GetByDispatchCenterIdAsync(string dispatchCenterId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.getbydispatchcenter", ActivityKind.Client);
            activity?.SetTag("app.dispatchCenterId", dispatchCenterId);
            var q = _users.GetItemQueryIterator<UserDoc>(
                new QueryDefinition("SELECT * FROM c WHERE c.dispatchCenterId = @dispatchCenterId")
                    .WithParameter("@dispatchCenterId", dispatchCenterId));
            return await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapUser, activity, _logger, "cosmos.users.getbydispatchcenter").ConfigureAwait(false);
        }

        private async Task<string> GetDocumentIdAsync(string userName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.getid", ActivityKind.Client);
            activity?.SetTag("app.userName", userName);
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT c.id FROM c WHERE c.userName = @u").WithParameter("@u", userName));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, d => d.id, activity, _logger, "cosmos.users.getid").ConfigureAwait(false);
        }

        public async Task<ApplicationUser> GetByUserNameAsync(string userName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.get", ActivityKind.Client);
            activity?.SetTag("app.userName", userName);
            // Use cross-partition query to find user by userName
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT * FROM c WHERE c.userName = @u").WithParameter("@u", userName));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, MapUser, activity, _logger, "cosmos.users.get").ConfigureAwait(false);
        }

        public async Task<ApplicationUser> GetByUpnAsync(string upn)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.getbyupn", ActivityKind.Client);
            activity?.SetTag("app.upn", upn);
            // Case-insensitive UPN matching using LOWER() function
            var q = _users.GetItemQueryIterator<UserDoc>(
                new QueryDefinition("SELECT * FROM c WHERE LOWER(c.upn) = LOWER(@upn)")
                    .WithParameter("@upn", upn));
            var result = await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, MapUser, activity, _logger, "cosmos.users.getbyupn").ConfigureAwait(false);
            if (result == null)
            {
                _logger.LogDebug("GetByUpn: No user found with upn={Upn}", upn);
            }
            return result;
        }

        public async Task UpsertAsync(ApplicationUser user)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.users.upsert", ActivityKind.Client);
            activity?.SetTag("app.userName", user.UserName);
            
            // Check if user already exists to preserve their ID
            var existing = await GetByUserNameAsync(user.UserName).ConfigureAwait(false);
            var documentId = existing != null ? await GetDocumentIdAsync(user.UserName).ConfigureAwait(false) : Guid.NewGuid().ToString();

            // Preserve PreferredLanguage when the caller doesn't set it (common in partial updates / admin flows)
            var preferredLanguage = PreferredLanguageMerger.Merge(user.PreferredLanguage, existing?.PreferredLanguage);
            
                var doc = new UserDoc 
            { 
                id = documentId,
                userName = user.UserName, 
                fullName = user.FullName, 
                avatar = user.Avatar, 
                preferredLanguage = preferredLanguage,
                email = user.Email, 
                mobile = user.MobileNumber, 
                enabled = user.Enabled, 
                upn = user.Upn,
                tenantId = user.TenantId,
                displayName = user.DisplayName,
                country = user.Country,
                region = user.Region,
                fixedRooms = user.FixedRooms != null ? System.Linq.Enumerable.ToArray(user.FixedRooms) : null, 
                dispatchCenterIds = user.DispatchCenterIds != null ? System.Linq.Enumerable.ToArray(user.DispatchCenterIds) : null,
                dispatchCenterId = user.DispatchCenterId,
                defaultRoom = user.DefaultRoom 
            };
            try
            {
                var resp = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _users.UpsertItemAsync(doc, new PartitionKey(doc.userName)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.users.upsert").ConfigureAwait(false);
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

        private static Room MapRoom(RoomDoc d)
        {
            return new Room
            {
                Id = DocIdUtil.TryParseRoomId(d.id),
                Name = d.name,
                DisplayName = d.displayName,
                RoomType = Enum.TryParse<RoomType>(d.roomType, out var roomType) ? roomType : RoomType.General,
                PairKey = d.pairKey,
                DispatchCenterAId = d.dispatchCenterAId,
                DispatchCenterBId = d.dispatchCenterBId,
                IsActive = d.isActive ?? true,
                Users = d.users != null ? new List<string>(d.users) : new List<string>(),
                Languages = d.languages != null ? new List<string>(d.languages) : new List<string>()
            };
        }

        public async Task<IEnumerable<Room>> GetAllAsync()
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getall", ActivityKind.Client);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c ORDER BY c.name"));
            var list = await CosmosQueryHelper.ExecutePaginatedQueryAsync(
                q,
                MapRoom,
                activity,
                _logger,
                "cosmos.rooms.getall");
            return list.OrderBy(r => r.Name).ToList();
        }

        public async Task<Room> GetByIdAsync(int id)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getbyid", ActivityKind.Client);
            activity?.SetTag("app.room.id", id);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(
                q,
                MapRoom,
                activity,
                _logger,
                "cosmos.rooms.getbyid");
        }

        public async Task<Room> GetByNameAsync(string name)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getbyname", ActivityKind.Client);
            activity?.SetTag("app.room", name);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.name = @n").WithParameter("@n", name));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(
                q,
                MapRoom,
                activity,
                _logger,
                "cosmos.rooms.getbyname");
        }

        public async Task<Room> GetByPairKeyAsync(string pairKey)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getbypairkey", ActivityKind.Client);
            activity?.SetTag("app.pairKey", pairKey);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.pairKey = @pairKey").WithParameter("@pairKey", pairKey));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, MapRoom, activity, _logger, "cosmos.rooms.getbypairkey").ConfigureAwait(false);
        }

        public async Task<IEnumerable<Room>> GetByDispatchCenterIdAsync(string dispatchCenterId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.rooms.getbydispatchcenter", ActivityKind.Client);
            activity?.SetTag("app.dispatchCenterId", dispatchCenterId);
            var q = _rooms.GetItemQueryIterator<RoomDoc>(
                new QueryDefinition("SELECT * FROM c WHERE c.isActive = true AND (c.dispatchCenterAId = @dispatchCenterId OR c.dispatchCenterBId = @dispatchCenterId)")
                    .WithParameter("@dispatchCenterId", dispatchCenterId));
            return await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapRoom, activity, _logger, "cosmos.rooms.getbydispatchcenter").ConfigureAwait(false);
        }

        public async Task UpsertAsync(Room room)
        {
            if (room == null) return;

            if (room.Id == 0)
            {
                room.Id = DocIdUtil.TryParseRoomId(room.Name ?? Guid.NewGuid().ToString("N"));
            }

            var doc = new RoomDoc
            {
                id = room.Id.ToString(),
                name = room.Name,
                displayName = room.DisplayName,
                roomType = room.RoomType.ToString(),
                pairKey = room.PairKey,
                dispatchCenterAId = room.DispatchCenterAId,
                dispatchCenterBId = room.DispatchCenterBId,
                isActive = room.IsActive,
                users = room.Users?.ToArray() ?? Array.Empty<string>(),
                languages = room.Languages?.ToArray() ?? Array.Empty<string>()
            };

            await _rooms.UpsertItemAsync(doc, new PartitionKey(doc.name)).ConfigureAwait(false);
        }

        public async Task AddUserToRoomAsync(string roomName, string userName)
        {
            await UpsertRoomUserAsync(roomName, userName, add: true).ConfigureAwait(false);
        }

        public async Task RemoveUserFromRoomAsync(string roomName, string userName)
        {
            await UpsertRoomUserAsync(roomName, userName, add: false).ConfigureAwait(false);
        }

        public async Task AddLanguageToRoomAsync(string roomName, string language)
        {
            await UpsertRoomLanguageAsync(roomName, language).ConfigureAwait(false);
        }

        private async Task<List<RoomDoc>> GetRoomDocsByNameAsync(string roomName)
        {
            var q = _rooms.GetItemQueryIterator<RoomDoc>(
                new QueryDefinition("SELECT * FROM c WHERE c.name = @n").WithParameter("@n", roomName));

            var rooms = new List<RoomDoc>();
            while (q.HasMoreResults)
            {
                var page = await Resilience.RetryHelper.ExecuteAsync(
                    _ => q.ReadNextAsync(),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.rooms.byname.forRoomRepo").ConfigureAwait(false);

                rooms.AddRange(page);

                // Defensive limit: a room name should be unique.
                if (rooms.Count >= 50) break;
            }

            if (rooms.Skip(1).Any())
            {
                _logger.LogWarning(
                    "Multiple room documents found for name {Room}. Merging users/languages to avoid data loss.",
                    LogSanitizer.Sanitize(roomName));
            }

            return rooms;
        }

        private async Task UpsertRoomUserAsync(string roomName, string userName, bool add)
        {
            var rooms = await GetRoomDocsByNameAsync(roomName).ConfigureAwait(false);
            if (rooms.Count == 0) return;

            var mergedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mergedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in rooms)
            {
                foreach (var u in r.users ?? Array.Empty<string>()) mergedUsers.Add(u);
                foreach (var l in r.languages ?? Array.Empty<string>()) mergedLanguages.Add(l);
            }

            var changed = add ? mergedUsers.Add(userName) : mergedUsers.Remove(userName);
            if (!changed) return;

            var room = rooms.FirstOrDefault(r => r.languages != null && r.languages.Length > 0) ?? rooms[0];
            room.users = mergedUsers.ToArray();
            room.languages = mergedLanguages.ToArray();

            await _rooms.UpsertItemAsync(room, new PartitionKey(roomName)).ConfigureAwait(false);
        }

        private async Task UpsertRoomLanguageAsync(string roomName, string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return;

            var rooms = await GetRoomDocsByNameAsync(roomName).ConfigureAwait(false);
            if (rooms.Count == 0) return;

            var mergedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mergedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rooms)
            {
                foreach (var u in r.users ?? Array.Empty<string>()) mergedUsers.Add(u);
                foreach (var l in r.languages ?? Array.Empty<string>()) mergedLanguages.Add(l);
            }

            if (!mergedLanguages.Add(language)) return;

            var room = rooms.FirstOrDefault(r => r.languages != null) ?? rooms[0];
            room.users = mergedUsers.ToArray();
            room.languages = mergedLanguages.ToArray();

            await _rooms.UpsertItemAsync(room, new PartitionKey(roomName)).ConfigureAwait(false);
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

        private static Message MapMessage(MessageDoc d)
        {
            return new Message
            {
                Id = int.Parse(d.id),
                Content = d.content,
                Timestamp = d.timestamp,
                ToRoom = new Room { Name = d.roomName },
                ToRoomId = 0,
                FromUser = new ApplicationUser { UserName = d.fromUser },
                FromDispatchCenterId = d.fromDispatchCenterId,
                ReadBy = d.readBy != null ? new List<string>(d.readBy) : new List<string>(),
                ReadByDispatchCenterIds = d.readByDispatchCenterIds != null ? new List<string>(d.readByDispatchCenterIds) : new List<string>(),
                EscalationStatus = Enum.TryParse<MessageEscalationStatus>(d.escalationStatus, out var escalationStatus) ? escalationStatus : MessageEscalationStatus.None,
                OpenEscalationId = d.openEscalationId,
                TranslationStatus = Enum.TryParse<TranslationStatus>(d.translationStatus, out var status) ? status : TranslationStatus.None,
                Translations = d.translations ?? new Dictionary<string, string>(),
                TranslationJobId = d.translationJobId,
                TranslationFailedAt = d.translationFailedAt,
                TranslationFailureCategory = Enum.TryParse<TranslationFailureCategory>(d.translationFailureCategory, out var cat) ? cat : TranslationFailureCategory.Unknown,
                TranslationFailureCode = Enum.TryParse<TranslationFailureCode>(d.translationFailureCode, out var code) ? code : TranslationFailureCode.Unknown,
                TranslationFailureMessage = d.translationFailureMessage
            };
        }

        public async Task<Message> CreateAsync(Message message)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.create", ActivityKind.Client);
            var room = message.ToRoom ?? await _roomsRepo.GetByIdAsync(message.ToRoomId).ConfigureAwait(false);
            var pk = room?.Name ?? "global";
            message.Id = message.Id == 0 ? new Random().Next(1, int.MaxValue) : message.Id;
            var doc = new MessageDoc 
            { 
                id = message.Id.ToString(), 
                roomName = pk, 
                content = message.Content, 
                fromUser = message.FromUser?.UserName, 
                fromDispatchCenterId = message.FromDispatchCenterId,
                timestamp = message.Timestamp, 
                readBy = (message.ReadBy != null ? message.ReadBy.ToArray() : Array.Empty<string>()),
                readByDispatchCenterIds = message.ReadByDispatchCenterIds != null ? message.ReadByDispatchCenterIds.ToArray() : Array.Empty<string>(),
                escalationStatus = message.EscalationStatus.ToString(),
                openEscalationId = message.OpenEscalationId,
                translationStatus = message.TranslationStatus.ToString(),
                translations = message.Translations,
                translationJobId = message.TranslationJobId,
                translationFailedAt = message.TranslationFailedAt,
                translationFailureCategory = message.TranslationStatus == TranslationStatus.Failed ? message.TranslationFailureCategory.ToString() : null,
                translationFailureCode = message.TranslationStatus == TranslationStatus.Failed ? message.TranslationFailureCode.ToString() : null,
                translationFailureMessage = message.TranslationFailureMessage
            };
            try
            {
                var resp = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _messages.UpsertItemAsync(doc, new PartitionKey(pk)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.create").ConfigureAwait(false);
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

        public async Task DeleteAsync(int id, string byUserName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.delete", ActivityKind.Client);
            var m = await GetByIdAsync(id).ConfigureAwait(false);
            if (m == null) return;
            if (m.FromUser?.UserName != byUserName) return;
            
            var room = m.ToRoom ?? await _roomsRepo.GetByIdAsync(m.ToRoomId).ConfigureAwait(false);
            var pk = room?.Name ?? "global";
            try
            {
                var resp = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _messages.DeleteItemAsync<MessageDoc>(id.ToString(), new PartitionKey(pk)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.delete").ConfigureAwait(false);
                activity?.SetTag("db.status_code", (int)resp.StatusCode);
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos message delete failed {Id}", id); // id is integer; no sanitization needed
                throw;
            }
        }

        public async Task<Message> GetByIdAsync(int id)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.getbyid", ActivityKind.Client);
            activity?.SetTag("app.message.id", id);
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, MapMessage, activity, _logger, "cosmos.messages.getbyid").ConfigureAwait(false);
        }

        public async Task<IEnumerable<Message>> GetRecentByRoomAsync(string roomName, int take = 20)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.recent", ActivityKind.Client);
            activity?.SetTag("app.room", roomName);
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition($"SELECT TOP {take} * FROM c WHERE c.roomName = @n ORDER BY c.timestamp DESC").WithParameter("@n", roomName), requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roomName) });
            var list = await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapMessage, activity, _logger, "cosmos.messages.recent").ConfigureAwait(false);
            return list.OrderBy(m => m.Timestamp).ToList();
        }

    public async Task<IEnumerable<Message>> GetBeforeByRoomAsync(string roomName, DateTime before, int take = 20)
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
            var list = await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapMessage, activity, _logger, "cosmos.messages.before").ConfigureAwait(false);
            // Keep only the newest 'take' items and return ascending order
            return list.OrderByDescending(m => m.Timestamp).Take(take).OrderBy(m => m.Timestamp).ToList();
        }

        public async Task<Message> MarkReadAsync(int id, string userName, string dispatchCenterId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.markread", ActivityKind.Client);
            activity?.SetTag("app.message.id", id);
            if (string.IsNullOrWhiteSpace(userName)) return null;
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            MessageDoc d = null;
            while (q.HasMoreResults && d == null)
            {
                var page = await Resilience.RetryHelper.ExecuteAsync(
                    _ => q.ReadNextAsync(),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.markread.lookup").ConfigureAwait(false);
                d = page.FirstOrDefault();
            }
            if (d == null) return null;
            var pk = d.roomName;
            var set = new HashSet<string>(d.readBy ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var readByDispatchCenters = new HashSet<string>(d.readByDispatchCenterIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var changed = set.Add(userName);
            if (!string.IsNullOrWhiteSpace(dispatchCenterId))
            {
                changed = readByDispatchCenters.Add(dispatchCenterId) || changed;
            }
            if (changed)
            {
                d.readBy = set.ToArray();
                d.readByDispatchCenterIds = readByDispatchCenters.ToArray();
                try
                {
                    var resp = await Resilience.RetryHelper.ExecuteAsync(
                        _ => _messages.UpsertItemAsync(d, new PartitionKey(pk)),
                        Transient.IsCosmosTransient,
                        _logger,
                        "cosmos.messages.markread.upsert").ConfigureAwait(false);
                    activity?.SetTag("db.status_code", (int)resp.StatusCode);
                }
                catch (CosmosException ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    // Rethrow with contextual information rather than only logging and rethrowing,
                    // to satisfy static analysis guidance and aid diagnostics upstream.
                    throw new InvalidOperationException(
                        $"Failed to mark message as read (Id={id}, Room={LogSanitizer.Sanitize(pk)}).",
                        ex);
                }
            }
            return MapMessage(d);
        }

        public async Task<Message> UpdateTranslationAsync(
            int id,
            MessageTranslationUpdate update)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.updatetranslation", ActivityKind.Client);
            activity?.SetTag("app.message.id", id);
            activity?.SetTag("app.translation.status", update.Status.ToString());
            
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            MessageDoc d = null;
            while (q.HasMoreResults && d == null)
            {
                var page = await Resilience.RetryHelper.ExecuteAsync(
                    _ => q.ReadNextAsync(),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.updatetranslation.lookup").ConfigureAwait(false);
                d = page.FirstOrDefault();
            }
            if (d == null) return null;
            
            var pk = d.roomName;
            
            // Update translation fields
            d.translationStatus = update.Status.ToString();
            d.translations = update.Translations ?? new Dictionary<string, string>();
            d.translationJobId = update.JobId;
            d.translationFailedAt = update.FailedAt;

            if (update.Status == TranslationStatus.Failed)
            {
                d.translationFailureCategory = update.FailureCategory?.ToString();
                d.translationFailureCode = update.FailureCode?.ToString();
                d.translationFailureMessage = update.FailureMessage;
            }
            else
            {
                d.translationFailureCategory = null;
                d.translationFailureCode = null;
                d.translationFailureMessage = null;
            }
            
            try
            {
                var resp = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _messages.UpsertItemAsync(d, new PartitionKey(pk)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.updatetranslation.upsert").ConfigureAwait(false);
                activity?.SetTag("db.status_code", (int)resp.StatusCode);
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw new InvalidOperationException(
                    $"Failed to update message translation (Id={id}, Room={LogSanitizer.Sanitize(pk)}).",
                    ex);
            }
            
            return MapMessage(d);
        }

        public async Task<Message> UpdateEscalationAsync(int id, MessageEscalationStatus status, string escalationId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.messages.updateescalation", ActivityKind.Client);
            activity?.SetTag("app.message.id", id);
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            MessageDoc d = null;
            while (q.HasMoreResults && d == null)
            {
                var page = await Resilience.RetryHelper.ExecuteAsync(
                    _ => q.ReadNextAsync(),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.messages.updateescalation.lookup").ConfigureAwait(false);
                d = page.FirstOrDefault();
            }

            if (d == null) return null;

            d.escalationStatus = status.ToString();
            d.openEscalationId = escalationId;
            await _messages.UpsertItemAsync(d, new PartitionKey(d.roomName)).ConfigureAwait(false);
            return MapMessage(d);
        }
    }

    public class CosmosDispatchCentersRepository : IDispatchCentersRepository
    {
        private readonly Container _dispatchCenters;
        private readonly ILogger<CosmosDispatchCentersRepository> _logger;

        public CosmosDispatchCentersRepository(CosmosClients clients, ILogger<CosmosDispatchCentersRepository> logger)
        {
            _dispatchCenters = clients.DispatchCenters;
            _logger = logger;
        }

        private static DispatchCenter MapDispatchCenter(DispatchCenterDoc d)
        {
            return new DispatchCenter
            {
                Id = d.id,
                Name = d.name,
                Country = d.country,
                IfMain = d.ifMain,
                CorrespondingDispatchCenterIds = d.correspondingDispatchCenterIds != null ? new List<string>(d.correspondingDispatchCenterIds) : new List<string>(),
                Users = d.users != null ? new List<string>(d.users) : new List<string>(),
                OfficerUserName = d.officerUserName
            };
        }

        public async Task<IEnumerable<DispatchCenter>> GetAllAsync()
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.dispatchcenters.getall", ActivityKind.Client);
            var q = _dispatchCenters.GetItemQueryIterator<DispatchCenterDoc>(new QueryDefinition("SELECT * FROM c ORDER BY c.name"));
            return await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapDispatchCenter, activity, _logger, "cosmos.dispatchcenters.getall").ConfigureAwait(false);
        }

        public async Task<DispatchCenter> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            using var activity = Tracing.ActivitySource.StartActivity("cosmos.dispatchcenters.getbyid", ActivityKind.Client);
            activity?.SetTag("app.dispatchcenter.id", id);

            try
            {
                var item = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _dispatchCenters.ReadItemAsync<DispatchCenterDoc>(id, new PartitionKey(id)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.dispatchcenters.getbyid").ConfigureAwait(false);
                return MapDispatchCenter(item.Resource);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<DispatchCenter> GetByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            using var activity = Tracing.ActivitySource.StartActivity("cosmos.dispatchcenters.getbyname", ActivityKind.Client);
            activity?.SetTag("app.dispatchcenter.name", name);
            var q = _dispatchCenters.GetItemQueryIterator<DispatchCenterDoc>(
                new QueryDefinition("SELECT * FROM c WHERE c.name = @n").WithParameter("@n", name));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, MapDispatchCenter, activity, _logger, "cosmos.dispatchcenters.getbyname").ConfigureAwait(false);
        }

        public async Task UpsertAsync(DispatchCenter dispatchCenter)
        {
            if (dispatchCenter == null) return;

            dispatchCenter.Id ??= Guid.NewGuid().ToString();

            using var activity = Tracing.ActivitySource.StartActivity("cosmos.dispatchcenters.upsert", ActivityKind.Client);
            activity?.SetTag("app.dispatchcenter.id", dispatchCenter.Id);

            var doc = new DispatchCenterDoc
            {
                id = dispatchCenter.Id,
                name = dispatchCenter.Name,
                country = dispatchCenter.Country,
                ifMain = dispatchCenter.IfMain,
                correspondingDispatchCenterIds = dispatchCenter.CorrespondingDispatchCenterIds?.ToArray() ?? Array.Empty<string>(),
                users = dispatchCenter.Users?.ToArray() ?? Array.Empty<string>(),
                officerUserName = dispatchCenter.OfficerUserName
            };

            try
            {
                var resp = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _dispatchCenters.UpsertItemAsync(doc, new PartitionKey(doc.id)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.dispatchcenters.upsert").ConfigureAwait(false);
                activity?.SetTag("db.status_code", (int)resp.StatusCode);
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos dispatch center upsert failed {DispatchCenterId}", LogSanitizer.Sanitize(dispatchCenter.Id));
                throw;
            }
        }

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            using var activity = Tracing.ActivitySource.StartActivity("cosmos.dispatchcenters.delete", ActivityKind.Client);
            activity?.SetTag("app.dispatchcenter.id", id);

            try
            {
                var resp = await Resilience.RetryHelper.ExecuteAsync(
                    _ => _dispatchCenters.DeleteItemAsync<DispatchCenterDoc>(id, new PartitionKey(id)),
                    Transient.IsCosmosTransient,
                    _logger,
                    "cosmos.dispatchcenters.delete").ConfigureAwait(false);
                activity?.SetTag("db.status_code", (int)resp.StatusCode);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
            catch (CosmosException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Cosmos dispatch center delete failed {DispatchCenterId}", LogSanitizer.Sanitize(id));
                throw;
            }
        }

        public async Task AssignUserAsync(string dispatchCenterId, string userName)
        {
            if (string.IsNullOrWhiteSpace(dispatchCenterId) || string.IsNullOrWhiteSpace(userName)) return;

            var dispatchCenter = await GetByIdAsync(dispatchCenterId).ConfigureAwait(false);
            if (dispatchCenter == null) return;

            var set = new HashSet<string>(dispatchCenter.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (!set.Add(userName)) return;

            dispatchCenter.Users = set.ToList();
            await UpsertAsync(dispatchCenter).ConfigureAwait(false);
        }

        public async Task UnassignUserAsync(string dispatchCenterId, string userName)
        {
            if (string.IsNullOrWhiteSpace(dispatchCenterId) || string.IsNullOrWhiteSpace(userName)) return;

            var dispatchCenter = await GetByIdAsync(dispatchCenterId).ConfigureAwait(false);
            if (dispatchCenter == null) return;

            var set = new HashSet<string>(dispatchCenter.Users ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (!set.Remove(userName)) return;

            dispatchCenter.Users = set.ToList();
            await UpsertAsync(dispatchCenter).ConfigureAwait(false);
        }
    }

    public class CosmosEscalationsRepository : IEscalationsRepository
    {
        private readonly Container _escalations;
        private readonly ILogger<CosmosEscalationsRepository> _logger;

        public CosmosEscalationsRepository(CosmosClients clients, ILogger<CosmosEscalationsRepository> logger)
        {
            _escalations = clients.Escalations;
            _logger = logger;
        }

        private static Escalation MapEscalation(EscalationDoc d)
        {
            return new Escalation
            {
                Id = d.id,
                RoomName = d.roomName,
                PairKey = d.pairKey,
                SourceDispatchCenterId = d.sourceDispatchCenterId,
                TargetDispatchCenterId = d.targetDispatchCenterId,
                TargetOfficerUserName = d.targetOfficerUserName,
                TriggerType = Enum.TryParse<EscalationTriggerType>(d.triggerType, out var triggerType) ? triggerType : EscalationTriggerType.Automatic,
                Status = Enum.TryParse<Models.EscalationStatus>(d.status, out var status) ? status : Models.EscalationStatus.Scheduled,
                CreatedAt = d.createdAt,
                DueAt = d.dueAt,
                EscalatedAt = d.escalatedAt,
                ResolvedAt = d.resolvedAt,
                CancelledAt = d.cancelledAt,
                CreatedByUserName = d.createdByUserName,
                MessageIds = d.messageIds != null ? new List<int>(d.messageIds) : new List<int>(),
                MessageSnapshots = d.messageSnapshots != null
                    ? d.messageSnapshots.Select(x => new EscalationMessageSnapshot
                    {
                        MessageId = x.messageId,
                        Content = x.content,
                        Timestamp = x.timestamp,
                        FromUserName = x.fromUserName,
                        FromDispatchCenterId = x.fromDispatchCenterId
                    }).ToList()
                    : new List<EscalationMessageSnapshot>()
            };
        }

        private static EscalationDoc MapEscalationDoc(Escalation escalation)
        {
            return new EscalationDoc
            {
                id = escalation.Id,
                roomName = escalation.RoomName,
                pairKey = escalation.PairKey,
                sourceDispatchCenterId = escalation.SourceDispatchCenterId,
                targetDispatchCenterId = escalation.TargetDispatchCenterId,
                targetOfficerUserName = escalation.TargetOfficerUserName,
                triggerType = escalation.TriggerType.ToString(),
                status = escalation.Status.ToString(),
                createdAt = escalation.CreatedAt,
                dueAt = escalation.DueAt,
                escalatedAt = escalation.EscalatedAt,
                resolvedAt = escalation.ResolvedAt,
                cancelledAt = escalation.CancelledAt,
                createdByUserName = escalation.CreatedByUserName,
                messageIds = escalation.MessageIds?.ToArray() ?? Array.Empty<int>(),
                messageSnapshots = escalation.MessageSnapshots?.Select(x => new EscalationMessageSnapshotDoc
                {
                    messageId = x.MessageId,
                    content = x.Content,
                    timestamp = x.Timestamp,
                    fromUserName = x.FromUserName,
                    fromDispatchCenterId = x.FromDispatchCenterId
                }).ToArray() ?? Array.Empty<EscalationMessageSnapshotDoc>()
            };
        }

        public async Task<Escalation> CreateAsync(Escalation escalation)
        {
            escalation.Id ??= Guid.NewGuid().ToString();
            var doc = MapEscalationDoc(escalation);
            await _escalations.UpsertItemAsync(doc, new PartitionKey(doc.roomName)).ConfigureAwait(false);
            return escalation;
        }

        public async Task<Escalation> GetByIdAsync(string id, string roomName)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(roomName)) return null;
            try
            {
                var response = await _escalations.ReadItemAsync<EscalationDoc>(id, new PartitionKey(roomName)).ConfigureAwait(false);
                return MapEscalation(response.Resource);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<IEnumerable<Escalation>> GetByRoomAsync(string roomName, int take = 50)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.escalations.byroom", ActivityKind.Client);
            var q = _escalations.GetItemQueryIterator<EscalationDoc>(
                new QueryDefinition($"SELECT TOP {take} * FROM c WHERE c.roomName = @roomName ORDER BY c.createdAt DESC")
                    .WithParameter("@roomName", roomName),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roomName) });
            return await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapEscalation, activity, _logger, "cosmos.escalations.byroom").ConfigureAwait(false);
        }

        public async Task<IEnumerable<Escalation>> GetDueScheduledAsync(DateTime dueBeforeUtc, int take = 100)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.escalations.duescheduled", ActivityKind.Client);
            var q = _escalations.GetItemQueryIterator<EscalationDoc>(
                new QueryDefinition($"SELECT TOP {take} * FROM c WHERE c.status = @status AND c.dueAt <= @dueBeforeUtc ORDER BY c.dueAt ASC")
                    .WithParameter("@status", Models.EscalationStatus.Scheduled.ToString())
                    .WithParameter("@dueBeforeUtc", dueBeforeUtc));
            return await CosmosQueryHelper.ExecutePaginatedQueryAsync(q, MapEscalation, activity, _logger, "cosmos.escalations.duescheduled").ConfigureAwait(false);
        }

        public async Task<Escalation> GetOpenByMessageIdAsync(int messageId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("cosmos.escalations.openbymessage", ActivityKind.Client);
            var q = _escalations.GetItemQueryIterator<EscalationDoc>(
                new QueryDefinition("SELECT TOP 1 * FROM c WHERE ARRAY_CONTAINS(c.messageIds, @messageId) AND (c.status = @scheduled OR c.status = @escalated)")
                    .WithParameter("@messageId", messageId)
                    .WithParameter("@scheduled", Models.EscalationStatus.Scheduled.ToString())
                    .WithParameter("@escalated", Models.EscalationStatus.Escalated.ToString()));
            return await CosmosQueryHelper.ExecuteSingleResultQueryAsync(q, MapEscalation, activity, _logger, "cosmos.escalations.openbymessage").ConfigureAwait(false);
        }

        public async Task UpsertAsync(Escalation escalation)
        {
            if (escalation == null) return;
            var doc = MapEscalationDoc(escalation);
            await _escalations.UpsertItemAsync(doc, new PartitionKey(doc.roomName)).ConfigureAwait(false);
        }
    }
}
