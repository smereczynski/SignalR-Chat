using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Chat.Web.Models;
using Chat.Web.Options;

namespace Chat.Web.Repositories
{
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
            Database = Client.GetDatabase(options.Database);
            Users = Database.GetContainer(options.UsersContainer);
            Rooms = Database.GetContainer(options.RoomsContainer);
            Messages = Database.GetContainer(options.MessagesContainer);
        }
    }

    // Simple DTOs for Cosmos storage
    internal class UserDoc { public string id { get; set; } public string userName { get; set; } public string fullName { get; set; } public string avatar { get; set; } }
    internal class RoomDoc { public string id { get; set; } public string name { get; set; } public string admin { get; set; } }
    internal class MessageDoc { public string id { get; set; } public string roomName { get; set; } public string content { get; set; } public string fromUser { get; set; } public DateTime timestamp { get; set; } }

    public class CosmosUsersRepository : IUsersRepository
    {
    private readonly Container _users;
        public CosmosUsersRepository(CosmosClients clients)
        {
            _users = clients.Users;
        }

        public IEnumerable<ApplicationUser> GetAll()
        {
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT * FROM c"));
            var list = new List<ApplicationUser>();
            while (q.HasMoreResults)
            {
                var page = q.ReadNextAsync().GetAwaiter().GetResult();
                list.AddRange(page.Select(d => new ApplicationUser { UserName = d.userName, FullName = d.fullName, Avatar = d.avatar }));
            }
            return list;
        }

        public ApplicationUser GetByUserName(string userName)
        {
            var q = _users.GetItemQueryIterator<UserDoc>(new QueryDefinition("SELECT * FROM c WHERE c.userName = @u").WithParameter("@u", userName));
            while (q.HasMoreResults)
            {
                var page = q.ReadNextAsync().GetAwaiter().GetResult();
                var d = page.FirstOrDefault();
                if (d != null) return new ApplicationUser { UserName = d.userName, FullName = d.fullName, Avatar = d.avatar };
            }
            return null;
        }

        public void Upsert(ApplicationUser user)
        {
            var doc = new UserDoc { id = user.UserName, userName = user.UserName, fullName = user.FullName, avatar = user.Avatar };
            _users.UpsertItemAsync(doc, new PartitionKey(doc.userName)).GetAwaiter().GetResult();
        }
    }

    public class CosmosRoomsRepository : IRoomsRepository
    {
    private readonly Container _rooms;
        public CosmosRoomsRepository(CosmosClients clients)
        {
            _rooms = clients.Rooms;
        }

        public Room Create(Room room)
        {
            // id as Guid string for cosmos; store legacy int in a field
            room.Id = room.Id == 0 ? new Random().Next(1, int.MaxValue) : room.Id;
            var doc = new RoomDoc { id = room.Id.ToString(), name = room.Name, admin = room.Admin?.UserName };
            _rooms.UpsertItemAsync(doc, new PartitionKey(doc.name)).GetAwaiter().GetResult();
            return room;
        }

        public void Delete(int id)
        {
            // Cannot delete without partition key; query then delete
            var r = GetById(id);
            if (r != null)
            {
                _rooms.DeleteItemAsync<RoomDoc>(id.ToString(), new PartitionKey(r.Name)).GetAwaiter().GetResult();
            }
        }

        public IEnumerable<Room> GetAll()
        {
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c ORDER BY c.name"));
            var list = new List<Room>();
            while (q.HasMoreResults)
            {
                var page = q.ReadNextAsync().GetAwaiter().GetResult();
                list.AddRange(page.Select(d => new Room { Id = int.Parse(d.id), Name = d.name, Admin = new ApplicationUser { UserName = d.admin } }));
            }
            return list.OrderBy(r => r.Name);
        }

        public Room GetById(int id)
        {
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
            while (q.HasMoreResults)
            {
                var page = q.ReadNextAsync().GetAwaiter().GetResult();
                var d = page.FirstOrDefault();
                if (d != null) return new Room { Id = int.Parse(d.id), Name = d.name, Admin = new ApplicationUser { UserName = d.admin } };
            }
            return null;
        }

        public Room GetByName(string name)
        {
            var q = _rooms.GetItemQueryIterator<RoomDoc>(new QueryDefinition("SELECT * FROM c WHERE c.name = @n").WithParameter("@n", name));
            while (q.HasMoreResults)
            {
                var page = q.ReadNextAsync().GetAwaiter().GetResult();
                var d = page.FirstOrDefault();
                if (d != null) return new Room { Id = int.Parse(d.id), Name = d.name, Admin = new ApplicationUser { UserName = d.admin } };
            }
            return null;
        }

        public void Update(Room room)
        {
            var doc = new RoomDoc { id = room.Id.ToString(), name = room.Name, admin = room.Admin?.UserName };
            _rooms.UpsertItemAsync(doc, new PartitionKey(doc.name)).GetAwaiter().GetResult();
        }
    }

    public class CosmosMessagesRepository : IMessagesRepository
    {
    private readonly Container _messages;
        private readonly IRoomsRepository _roomsRepo;

        public CosmosMessagesRepository(CosmosClients clients, IRoomsRepository roomsRepo)
        {
            _messages = clients.Messages;
            _roomsRepo = roomsRepo;
        }

        public Message Create(Message message)
        {
            var room = message.ToRoom ?? _roomsRepo.GetById(message.ToRoomId);
            var pk = room?.Name ?? "global";
            message.Id = message.Id == 0 ? new Random().Next(1, int.MaxValue) : message.Id;
            var doc = new MessageDoc { id = message.Id.ToString(), roomName = pk, content = message.Content, fromUser = message.FromUser?.UserName, timestamp = message.Timestamp };
            _messages.UpsertItemAsync(doc, new PartitionKey(pk)).GetAwaiter().GetResult();
            return message;
        }

        public void Delete(int id, string byUserName)
        {
            var m = GetById(id);
            if (m?.FromUser?.UserName == byUserName)
            {
                var room = m.ToRoom ?? _roomsRepo.GetById(m.ToRoomId);
                var pk = room?.Name ?? "global";
                _messages.DeleteItemAsync<MessageDoc>(id.ToString(), new PartitionKey(pk)).GetAwaiter().GetResult();
            }
        }

        public Message GetById(int id)
        {
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id").WithParameter("@id", id.ToString()));
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

        public IEnumerable<Message> GetRecentByRoom(string roomName, int take = 20)
        {
            var q = _messages.GetItemQueryIterator<MessageDoc>(new QueryDefinition($"SELECT TOP {take} * FROM c WHERE c.roomName = @n ORDER BY c.timestamp DESC").WithParameter("@n", roomName), requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(roomName) });
            var list = new List<Message>();
            while (q.HasMoreResults)
            {
                var page = q.ReadNextAsync().GetAwaiter().GetResult();
                list.AddRange(page.Select(d => new Message { Id = int.Parse(d.id), Content = d.content, Timestamp = d.timestamp, ToRoom = new Room { Name = d.roomName }, FromUser = new ApplicationUser { UserName = d.fromUser } }));
            }
            return list.OrderBy(m => m.Timestamp);
        }
    }
}
