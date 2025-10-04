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
    /// </summary>
    public class ChatHub : Hub
    {
        /// <summary>
        /// Active connections with lightweight user information (room participation tracked per user).
        /// </summary>
        public static readonly List<UserViewModel> _Connections = new List<UserViewModel>();

        /// <summary>
        /// Map userName -> current SignalR connection id (latest). Allows targeted messaging.
        /// </summary>
        private static readonly Dictionary<string, string> _ConnectionsMap = new Dictionary<string, string>();

        private readonly Repositories.IUsersRepository _users;
        private readonly ILogger<ChatHub> _logger;
        private readonly Services.IInProcessMetrics _metrics;

        /// <summary>
        /// Creates a new Hub instance.
        /// </summary>
        public ChatHub(Repositories.IUsersRepository users, ILogger<ChatHub> logger, Services.IInProcessMetrics metrics)
        {
            _users = users;
            _logger = logger;
            _metrics = metrics;
        }

        /// <summary>
        /// Sends a private (direct) message to a single user. Validates both sender and recipient existence.
        /// </summary>
        /// <param name="receiverName">Target recipient user name.</param>
        /// <param name="message">Message body supplied by the caller (HTML tags stripped).</param>
        public async Task SendPrivate(string receiverName, string message)
        {
            if (_ConnectionsMap.TryGetValue(receiverName, out string userId))
            {
                var sender = _Connections.First(u => u.UserName == IdentityName);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    var messageViewModel = new MessageViewModel
                    {
                        Content = Regex.Replace(message, @"<.*?>", string.Empty),
                        FromUserName = sender.UserName,
                        FromFullName = sender.FullName,
                        Avatar = sender.Avatar,
                        Room = string.Empty,
                        Timestamp = DateTime.Now
                    };

                    await Clients.Client(userId).SendAsync("newMessage", messageViewModel);
                    await Clients.Caller.SendAsync("newMessage", messageViewModel);
                    _metrics.IncMessagesSent();
                }
            }
        }

        /// <summary>
        /// Adds the calling connection to the specified room (SignalR group) and notifies existing members.
        /// Increments a custom counter for room joins.
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
                var user = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
                if (user != null && user.CurrentRoom != roomName)
                {
                    var previous = user.CurrentRoom;
                    if (!string.IsNullOrEmpty(previous))
                    {
                        await Clients.OthersInGroup(previous).SendAsync("removeUser", user);
                        _logger.LogDebug("User {User} leaving room {Prev}", IdentityName, previous);
                    }
                    await Leave(previous);
                    await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
                    user.CurrentRoom = roomName;
                    await Clients.OthersInGroup(roomName).SendAsync("addUser", user);
                    _logger.LogInformation("User {User} joined room {Room}", IdentityName, roomName);
                    _metrics.IncRoomsJoined();
                }
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
            var users = _Connections.Where(u => u.CurrentRoom == roomName).ToList();
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
                var existing = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
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
                        Device = GetDevice(),
                        CurrentRoom = string.Empty
                    };
                    _Connections.Add(userViewModel);
                    _ConnectionsMap[IdentityName] = Context.ConnectionId;
                    _logger.LogInformation("User connected {User}", IdentityName);
                    _metrics.IncActiveConnections();
                    await Clients.Caller.SendAsync("getProfileInfo", userViewModel);
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
                var user = _Connections.FirstOrDefault(u => u.UserName == IdentityName);
                if (user == null)
                {
                    _logger.LogDebug("Disconnect for unknown user {User}", IdentityName);
                    await base.OnDisconnectedAsync(exception);
                    return;
                }
                _Connections.Remove(user);
                if (!string.IsNullOrWhiteSpace(user.CurrentRoom))
                {
                    await Clients.OthersInGroup(user.CurrentRoom).SendAsync("removeUser", user);
                }
                _ConnectionsMap.Remove(user.UserName);
                _logger.LogInformation("User disconnected {User}", IdentityName);
                _metrics.DecActiveConnections();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnDisconnected failure {User}", IdentityName);
                await Clients.Caller.SendAsync("onError", "OnDisconnected: " + ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            await base.OnDisconnectedAsync(exception);
        }

        private string IdentityName => Context.User.Identity.Name;

        private string GetDevice()
        {
            var device = Context.GetHttpContext().Request.Headers["Device"].ToString();
            if (!string.IsNullOrEmpty(device) && (device.Equals("Desktop") || device.Equals("Mobile")))
                return device;
            return "Web";
        }
    }
}
