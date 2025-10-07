using Chat.Web.Models;
using Chat.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Chat.Web.Observability;
using System.Diagnostics;

namespace Chat.Web.Hubs
{
    [Authorize]
    /// <summary>
    /// SignalR hub handling real-time chat operations: user presence, room membership and message broadcast.
    /// Tracks connected users for quick lookup and updates custom OpenTelemetry counters.
    /// Private direct messaging removed; connection map retained for future notification use-cases.
    /// </summary>
    public class ChatHub : Hub
    {
        /// <summary>
        /// Active connections with lightweight user information (room participation tracked per user).
        /// </summary>
    public static readonly List<UserViewModel> _Connections = new List<UserViewModel>();
    internal static readonly object _ConnectionsLock = new object();

        /// <summary>
        /// Map userName -> current SignalR connection id (latest). Allows targeted messaging.
        /// </summary>
        private static readonly Dictionary<string, string> _ConnectionsMap = new Dictionary<string, string>();

    private readonly Repositories.IUsersRepository _users;
    private readonly Repositories.IMessagesRepository _messages;
    private readonly Repositories.IRoomsRepository _rooms;
        private readonly ILogger<ChatHub> _logger;
        private readonly Services.IInProcessMetrics _metrics;

        /// <summary>
        /// Creates a new Hub instance.
        /// </summary>
        public ChatHub(Repositories.IUsersRepository users,
            Repositories.IMessagesRepository messages,
            Repositories.IRoomsRepository rooms,
            ILogger<ChatHub> logger,
            Services.IInProcessMetrics metrics)
        {
            _users = users;
            _messages = messages;
            _rooms = rooms;
            _logger = logger;
            _metrics = metrics;
        }

        /// <summary>
        /// Adds the calling connection to the specified room (SignalR group) and notifies existing members.
        /// Enforces fixed room membership if user profile defines FixedRooms.
        /// </summary>
        public async Task Join(string roomName)
        {
            var activity = Tracing.ActivitySource.StartActivity("ChatHub.Join");
            activity?.SetTag("chat.room", roomName);
            if (string.IsNullOrWhiteSpace(roomName))
            {
                _logger.LogWarning("Join called with empty room by {User}", IdentityName);
                await Clients.Caller.SendAsync("onError", "You failed to join the chat room! Room name is required.");
                activity?.SetStatus(ActivityStatusCode.Error, "empty room");
                activity?.Dispose();
                return;
            }
            try
            {
                var profile = _users.GetByUserName(IdentityName);
                bool hasFixed = profile?.FixedRooms != null;
                bool hasAny = hasFixed && profile.FixedRooms.Any();
                if (!hasAny)
                {
                    _logger.LogWarning("Unauthorized room join attempt (no fixed rooms) {User} => {Room} correlation={CorrelationId}", IdentityName, roomName, Context.ConnectionId);
                    await Clients.Caller.SendAsync("onError", "You are not authorized for this room.");
                    activity?.SetStatus(ActivityStatusCode.Error, "room unauthorized none");
                    return;
                }
                if (!profile.FixedRooms.Contains(roomName))
                {
                    _logger.LogWarning("Unauthorized room join attempt (not in fixed list) {User} => {Room} allowed={AllowedRooms} correlation={CorrelationId}", IdentityName, roomName, string.Join(',', profile.FixedRooms), Context.ConnectionId);
                    await Clients.Caller.SendAsync("onError", "You are not authorized for this room.");
                    activity?.SetStatus(ActivityStatusCode.Error, "room unauthorized");
                    return;
                }
                UserViewModel user;
                lock (_ConnectionsLock)
                {
                    user = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
                }
                // If user not yet tracked (race with OnConnected) create an entry.
                if (user == null)
                {
                    user = new UserViewModel
                    {
                        UserName = profile?.UserName ?? IdentityName,
                        FullName = profile?.FullName ?? IdentityName,
                        Avatar = profile?.Avatar,
                        Device = GetDevice(),
                        CurrentRoom = string.Empty
                    };
                    lock (_ConnectionsLock)
                    {
                        _Connections.Add(user);
                    }
                    _ConnectionsMap[IdentityName] = Context.ConnectionId;
                }
                if (user.CurrentRoom == roomName)
                {
                    // Already in requested room; no-op
                    return;
                }
                var previous = user.CurrentRoom;
                if (!string.IsNullOrEmpty(previous))
                {
                    await Clients.OthersInGroup(previous).SendAsync("removeUser", user);
                    _logger.LogDebug("User {User} leaving room {Prev}", IdentityName, previous);
                    activity?.AddEvent(new ActivityEvent("leave", tags: new ActivityTagsCollection {{"chat.room.prev", previous}}));
                    _metrics.DecRoomPresence(previous);
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous);
                }
                await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
                lock (_ConnectionsLock)
                {
                    user.CurrentRoom = roomName;
                }
                await Clients.OthersInGroup(roomName).SendAsync("addUser", user);
                _logger.LogInformation("User {User} joined room {Room}", IdentityName, roomName);
                _metrics.IncRoomsJoined();
                _metrics.IncRoomPresence(roomName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Join failed for user {User} room {Room}", IdentityName, roomName);
                await Clients.Caller.SendAsync("onError", "You failed to join the chat room!" + ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            finally
            {
                activity?.Dispose();
            }
        }

        /// <summary>
        /// Removes the calling connection from a room and notifies the remaining members.
        /// </summary>
        public async Task Leave(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName)) return;
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.Leave");
            activity?.SetTag("chat.room", roomName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
            _logger.LogDebug("User {User} left room {Room}", IdentityName, roomName);
        }

        /// <summary>
        /// Returns all connected users in a given room (in-memory snapshot).
        /// </summary>
        public IEnumerable<UserViewModel> GetUsers(string roomName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.GetUsers");
            activity?.SetTag("chat.room", roomName);
            if (string.IsNullOrWhiteSpace(roomName)) return Enumerable.Empty<UserViewModel>();
                List<UserViewModel> users;
                lock (_ConnectionsLock)
                {
                    users = _Connections.Where(u => u.CurrentRoom == roomName).ToList();
                }
            activity?.SetTag("chat.user.count", users.Count);
            return users;
        }

        /// <summary>
        /// Lifecycle hook: new client connection established. Adds/updates user state and increments active connection counter.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.OnConnected");
            try
            {
                var user = _users.GetByUserName(IdentityName);
                UserViewModel existing;
                lock (_ConnectionsLock)
                {
                    existing = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
                }
                var device = GetDevice();
                if (existing != null)
                {
                    _ConnectionsMap[IdentityName] = Context.ConnectionId;
                    activity?.SetTag("chat.duplicateConnection", true);
                    _logger.LogDebug("Duplicate connection for {User} reassigned connectionId", IdentityName);
                    await Clients.Caller.SendAsync("getProfileInfo", existing);
                }
                else
                {
                    var userViewModel = new UserViewModel
                    {
                        UserName = user?.UserName ?? IdentityName,
                        FullName = user?.FullName ?? IdentityName,
                        Avatar = user?.Avatar,
                        Device = device,
                        CurrentRoom = string.Empty
                    };
                    lock (_ConnectionsLock)
                    {
                        _Connections.Add(userViewModel);
                    }
                    _ConnectionsMap[IdentityName] = Context.ConnectionId;
                    _logger.LogInformation("User connected {User}", IdentityName);
                    _metrics.IncActiveConnections();
                    _metrics.UserAvailable(IdentityName, device);
                    await Clients.Caller.SendAsync("getProfileInfo", userViewModel);

                    // Auto-join default room logic
                    try
                    {
                        var fixedRooms = user?.FixedRooms ?? new System.Collections.Generic.List<string>();
                        if (fixedRooms.Any())
                        {
                            string target = null;
                            string strategy = null;
                            if (!string.IsNullOrWhiteSpace(user?.DefaultRoom) && fixedRooms.Contains(user.DefaultRoom))
                            {
                                target = user.DefaultRoom;
                                strategy = "autoJoin.default";
                            }
                            else if (fixedRooms.Count == 1)
                            {
                                target = fixedRooms.First();
                                strategy = "autoJoin.single";
                            }
                            else
                            {
                                target = fixedRooms.OrderBy(r => r).First();
                                strategy = "autoJoin.firstAlphabetical";
                            }
                            if (!string.IsNullOrEmpty(target))
                            {
                                Tracing.ActivitySource.StartActivity(strategy)?.Dispose();
                                _ = Join(target); // fire & forget
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-join default room failed for {User}", IdentityName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnConnected failure {User}", IdentityName);
                await Clients.Caller.SendAsync("onError", "OnConnected:" + ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Lifecycle hook: client disconnected. Removes mapping and decrements active connection counter.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.OnDisconnected");
            try
            {
                UserViewModel user;
                lock (_ConnectionsLock)
                {
                    user = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
                }
                if (user == null)
                {
                    _logger.LogDebug("Disconnect for unknown user {User}", IdentityName);
                    await base.OnDisconnectedAsync(exception);
                    return;
                }
                lock (_ConnectionsLock)
                {
                    _Connections.Remove(user);
                }
                if (!string.IsNullOrWhiteSpace(user.CurrentRoom))
                {
                    await Clients.OthersInGroup(user.CurrentRoom).SendAsync("removeUser", user);
                    _metrics.DecRoomPresence(user.CurrentRoom);
                }
                _ConnectionsMap.Remove(user.UserName);
                _logger.LogInformation("User disconnected {User}", IdentityName);
                _metrics.DecActiveConnections();
                _metrics.UserUnavailable(IdentityName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnDisconnected failure {User}", IdentityName);
                await Clients.Caller.SendAsync("onError", "OnDisconnected: " + ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Sends a direct notification (non-chat message) to a single connected user if online.
        /// </summary>
        public Task NotifyUser(string targetUserName, string title, string body)
        {
            if (string.IsNullOrWhiteSpace(targetUserName) || string.IsNullOrWhiteSpace(title))
                return Task.CompletedTask;
            if (_ConnectionsMap.TryGetValue(targetUserName, out var connId))
            {
                return Clients.Client(connId).SendAsync("notify", new { title, body, from = IdentityName, ts = DateTime.UtcNow });
            }
            return Task.CompletedTask;
        }

        private string IdentityName => Context.User.Identity.Name;

        private string GetDevice()
        {
            var device = Context.GetHttpContext().Request.Headers["Device"].ToString();
            if (!string.IsNullOrEmpty(device) && (device.Equals("Desktop") || device.Equals("Mobile")))
                return device;
            return "Web";
        }

        /// <summary>
        /// Returns a thread-safe deep snapshot of current connections (used externally for presence API).
        /// </summary>
        public static IReadOnlyList<UserViewModel> Snapshot()
        {
            lock (_ConnectionsLock)
            {
                return _Connections.Select(c => new UserViewModel
                {
                    UserName = c.UserName,
                    FullName = c.FullName,
                    Avatar = c.Avatar,
                    CurrentRoom = c.CurrentRoom,
                    Device = c.Device
                }).ToList();
            }
        }

        /// <summary>
        /// Hub-based message send to the caller's current room. CorrelationId flows from client for optimistic reconciliation.
        /// </summary>
        public async Task SendMessage(string content, string correlationId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.SendMessage");
            activity?.SetTag("chat.correlationId", correlationId);
            activity?.SetTag("chat.content.length", content?.Length ?? 0);
            if (string.IsNullOrWhiteSpace(content)) { activity?.AddEvent(new ActivityEvent("empty_content")); return; }
            UserViewModel user;
            lock (_ConnectionsLock)
            {
                user = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
            }
            if (user == null || string.IsNullOrEmpty(user.CurrentRoom))
            {
                _logger.LogWarning("SendMessage denied (no room) user={User}", IdentityName);
                activity?.SetStatus(ActivityStatusCode.Error, "no_room");
                await Clients.Caller.SendAsync("onError", "You are not in a room.");
                return;
            }
            var domainUser = _users.GetByUserName(IdentityName);
            if (domainUser?.FixedRooms != null && domainUser.FixedRooms.Any() && !domainUser.FixedRooms.Contains(user.CurrentRoom))
            {
                _logger.LogWarning("SendMessage unauthorized user={User} room={Room}", IdentityName, user.CurrentRoom);
                activity?.SetStatus(ActivityStatusCode.Error, "unauthorized");
                await Clients.Caller.SendAsync("onError", "Not authorized for this room.");
                return;
            }
            var room = _rooms.GetByName(user.CurrentRoom);
            if (room == null)
            {
                _logger.LogWarning("SendMessage room missing user={User} room={Room}", IdentityName, user.CurrentRoom);
                activity?.SetStatus(ActivityStatusCode.Error, "room_missing");
                await Clients.Caller.SendAsync("onError", "Room no longer exists.");
                return;
            }
            // Basic sanitization (strip tags)
            var sanitized = System.Text.RegularExpressions.Regex.Replace(content, @"<.*?>", string.Empty);
            activity?.SetTag("chat.room", room.Name);
            activity?.SetTag("chat.sanitized.length", sanitized.Length);
            var msg = new Models.Message
            {
                Content = sanitized,
                FromUser = domainUser,
                ToRoom = room,
                Timestamp = System.DateTime.UtcNow
            };
            try
            {
                msg = _messages.Create(msg);
            }
            catch (System.Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "SendMessage persistence failed user={User} room={Room}", IdentityName, room.Name);
                await Clients.Caller.SendAsync("onError", "Failed to persist message.");
                return;
            }
            var vm = new ViewModels.MessageViewModel
            {
                Id = msg.Id,
                Content = msg.Content,
                FromUserName = msg.FromUser?.UserName,
                FromFullName = msg.FromUser?.FullName,
                Avatar = msg.FromUser?.Avatar,
                Room = room.Name,
                Timestamp = msg.Timestamp,
                CorrelationId = correlationId
            };
            await Clients.Group(room.Name).SendAsync("newMessage", vm);
            _metrics.IncMessagesSent();
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
    }
}
