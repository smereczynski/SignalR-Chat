using Chat.Web.Models;
using Chat.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Chat.Web.Observability;
using System.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chat.Web.Hubs
{
    [Authorize]
    /// <summary>
    /// SignalR hub handling real-time chat operations: user presence, room membership and message broadcast.
    /// Uses Context.Items for per-connection state and Redis for distributed presence snapshot.
    /// </summary>
    public class ChatHub : Hub
    {
        // Track active connection counts per user to avoid removing presence when alternate connections remain
        private static readonly ConcurrentDictionary<string, int> _UserConnectionCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private readonly Repositories.IUsersRepository _users;
    private readonly Repositories.IMessagesRepository _messages;
    private readonly Repositories.IRoomsRepository _rooms;
        private readonly Services.UnreadNotificationScheduler _unreadScheduler;
        private readonly ILogger<ChatHub> _logger;
        private readonly Services.IInProcessMetrics _metrics;
        private readonly Services.IMarkReadRateLimiter _markReadRateLimiter;
        private readonly Services.IPresenceTracker _presenceTracker;
        private readonly IStringLocalizer<Resources.SharedResources> _localizer;
        private readonly HealthCheckService _healthCheckService;

        /// <summary>
        /// Creates a new Hub instance.
        /// </summary>
        public ChatHub(Repositories.IUsersRepository users,
            Repositories.IMessagesRepository messages,
            Repositories.IRoomsRepository rooms,
            ILogger<ChatHub> logger,
            Services.IInProcessMetrics metrics,
            Services.UnreadNotificationScheduler unreadScheduler,
            Services.IMarkReadRateLimiter markReadRateLimiter,
            Services.IPresenceTracker presenceTracker,
            IStringLocalizer<Resources.SharedResources> localizer,
            HealthCheckService healthCheckService)
        {
            _users = users;
            _messages = messages;
            _rooms = rooms;
            _logger = logger;
            _metrics = metrics;
            _unreadScheduler = unreadScheduler;
            _markReadRateLimiter = markReadRateLimiter;
            _presenceTracker = presenceTracker;
            _localizer = localizer;
            _healthCheckService = healthCheckService;
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
                _logger.LogWarning("Join called with empty roomName by {User}", IdentityName);
                await Clients.Caller.SendAsync("onError", _localizer["ErrorJoinRoomNameRequired"].Value);
                activity?.SetStatus(ActivityStatusCode.Error, "invalid roomName");
                activity?.Dispose();
                return;
            }
            try
            {
                var profile = await _users.GetByUserNameAsync(IdentityName);
                bool hasFixed = profile?.FixedRooms != null;
                bool hasAny = hasFixed && profile.FixedRooms.Any();
                if (!hasAny)
                {
                    _logger.LogWarning("Unauthorized room join attempt (no fixed rooms) {User} => {Room} correlation={CorrelationId}", IdentityName, roomName, Context.ConnectionId);
                    await Clients.Caller.SendAsync("onError", _localizer["ErrorNotAuthorizedRoom"].Value);
                    activity?.SetStatus(ActivityStatusCode.Error, "room unauthorized none");
                    return;
                }
                if (!profile.FixedRooms.Contains(roomName))
                {
                    _logger.LogWarning("Unauthorized room join attempt (not in fixed list) {User} => {Room} allowed={AllowedRooms} correlation={CorrelationId}", IdentityName, roomName, string.Join(',', profile.FixedRooms), Context.ConnectionId);
                    await Clients.Caller.SendAsync("onError", _localizer["ErrorNotAuthorizedRoom"].Value);
                    activity?.SetStatus(ActivityStatusCode.Error, "room unauthorized");
                    return;
                }
                
                // Get user from Context.Items (per-connection state)
                var user = Context.Items["UserProfile"] as UserViewModel;
                var previous = Context.Items["CurrentRoom"] as string;
                
                if (user == null)
                {
                    _logger.LogWarning("Join called but UserProfile not in Context for {User}", IdentityName);
                    await Clients.Caller.SendAsync("onError", _localizer["ErrorUserProfileNotFound"].Value);
                    return;
                }
                
                if (previous == roomName)
                {
                    // Already in requested room; no-op
                    return;
                }
                
                if (!string.IsNullOrEmpty(previous))
                {
                    await Clients.OthersInGroup(previous).SendAsync("removeUser", user);
                    _logger.LogDebug("User {User} leaving room {Prev}", IdentityName, previous);
                    activity?.AddEvent(new ActivityEvent("leave", tags: new ActivityTagsCollection {{"chat.room.prev", previous}}));
                    _metrics.DecRoomPresence(previous);
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous);
                }
                
                await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
                
                // Update Context.Items for this connection
                Context.Items["CurrentRoom"] = roomName;
                user.CurrentRoom = roomName;
                
                // Update Redis presence (for cross-instance snapshot)
                await _presenceTracker.SetUserRoomAsync(IdentityName, user.FullName, user.Avatar, roomName);
                
                // Broadcast to the entire group (including the caller)
                await Clients.Group(roomName).SendAsync("addUser", user);
                
                // Send full presence snapshot to caller (get from Redis for cross-instance consistency)
                var allUsers = await _presenceTracker.GetAllUsersAsync();
                var presenceSnapshot = allUsers
                    .Where(u => u.CurrentRoom == roomName)
                    .Select(u => new { u.UserName, u.FullName, u.Avatar, u.CurrentRoom })
                    .ToList();
                await Clients.Caller.SendAsync("presenceSnapshot", presenceSnapshot);
                
                _logger.LogInformation("User {User} joined room {Room}", IdentityName, roomName);
                _metrics.IncRoomsJoined();
                _metrics.IncRoomPresence(roomName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Join failed for user {User} room {Room}", IdentityName, roomName);
                await Clients.Caller.SendAsync("onError", _localizer["ErrorJoinRoom", ex.Message].Value);
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
        /// Returns all connected users in a given room from distributed presence tracker.
        /// </summary>
        public async Task<IEnumerable<UserViewModel>> GetUsers(string roomName)
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.GetUsers");
            activity?.SetTag("chat.room", roomName);
            if (string.IsNullOrWhiteSpace(roomName)) return Enumerable.Empty<UserViewModel>();
            
            var allUsers = await _presenceTracker.GetAllUsersAsync();
            var users = allUsers.Where(u => u.CurrentRoom == roomName).ToList();
            activity?.SetTag("chat.user.count", users.Count);
            return users;
        }

        /// <summary>
        /// Lifecycle hook: new client connection established. Stores user profile in Context.Items.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.OnConnected");
            try
            {
                var user = await _users.GetByUserNameAsync(IdentityName);
                
                var userViewModel = new UserViewModel
                {
                    UserName = user?.UserName ?? IdentityName,
                    FullName = user?.FullName ?? IdentityName,
                    Avatar = user?.Avatar,
                    CurrentRoom = string.Empty
                };
                
                // Store user profile in Context.Items for fast access (per-connection state)
                Context.Items["UserProfile"] = userViewModel;
                Context.Items["CurrentRoom"] = string.Empty;
                
                // Increment connection count
                _UserConnectionCounts.AddOrUpdate(IdentityName, 1, (key, oldValue) => oldValue + 1);
                
                // Update Redis presence (for cross-instance snapshot API)
                await _presenceTracker.SetUserRoomAsync(
                    userViewModel.UserName,
                    userViewModel.FullName,
                    userViewModel.Avatar,
                    string.Empty);
                
                _logger.LogInformation("User connected {User}", IdentityName);
                _metrics.IncActiveConnections();
                _metrics.UserAvailable(IdentityName);
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
                            await Join(target);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Auto-join default room failed for {User}", IdentityName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnConnected failure {User}", IdentityName);
                await Clients.Caller.SendAsync("onError", _localizer["ErrorOccurred"].Value);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Lifecycle hook: client disconnected. Removes presence when last connection closes.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.OnDisconnected");
            try
            {
                // Get user from Context.Items
                var user = Context.Items["UserProfile"] as UserViewModel;
                var currentRoom = Context.Items["CurrentRoom"] as string;
                
                int remainingConnections = 0;
                // Decrement connection count atomically
                if (_UserConnectionCounts.TryGetValue(IdentityName, out var cnt))
                {
                    cnt = Math.Max(0, cnt - 1);
                    if (cnt == 0)
                    {
                        _UserConnectionCounts.TryRemove(IdentityName, out _);
                    }
                    else
                    {
                        _UserConnectionCounts[IdentityName] = cnt;
                    }
                    remainingConnections = cnt;
                }
                
                if (user == null)
                {
                    _logger.LogDebug("Disconnect for unknown user {User}", IdentityName);
                    await base.OnDisconnectedAsync(exception);
                    return;
                }
                
                // Only remove presence when the last connection closes
                if (remainingConnections <= 0)
                {
                    if (!string.IsNullOrWhiteSpace(currentRoom))
                    {
                        await Clients.OthersInGroup(currentRoom).SendAsync("removeUser", user);
                        _metrics.DecRoomPresence(currentRoom);
                    }
                    
                    // Remove from Redis
                    await _presenceTracker.RemoveUserAsync(IdentityName);
                    
                    _logger.LogInformation("User disconnected {User}", IdentityName);
                    _metrics.DecActiveConnections();
                    _metrics.UserUnavailable(IdentityName);
                }
                else
                {
                    _logger.LogDebug("User {User} has {Count} remaining connections; presence retained", IdentityName, remainingConnections);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnDisconnected failure {User}", IdentityName);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            await base.OnDisconnectedAsync(exception);
        }

        private string IdentityName => Context.User.Identity.Name;

        // Device indicator removed as unused.

        /// <summary>
        /// Returns a snapshot of current connections from distributed presence tracker (used externally for presence API).
        /// </summary>
        public async Task<IReadOnlyList<UserViewModel>> SnapshotAsync()
        {
            return await _presenceTracker.GetAllUsersAsync();
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
            
            // Get user and room from Context.Items (per-connection state - no Redis query needed!)
            var user = Context.Items["UserProfile"] as UserViewModel;
            var currentRoom = Context.Items["CurrentRoom"] as string;
            
            if (user == null || string.IsNullOrEmpty(currentRoom))
            {
                _logger.LogWarning("SendMessage denied (no room) user={User}", IdentityName);
                activity?.SetStatus(ActivityStatusCode.Error, "no_room");
                await Clients.Caller.SendAsync("onError", _localizer["ErrorNotInRoom"].Value);
                return;
            }
            
            var domainUser = await _users.GetByUserNameAsync(IdentityName);
            if (domainUser?.FixedRooms != null && domainUser.FixedRooms.Any() && !domainUser.FixedRooms.Contains(currentRoom))
            {
                _logger.LogWarning("SendMessage unauthorized user={User} room={Room}", IdentityName, currentRoom);
                activity?.SetStatus(ActivityStatusCode.Error, "unauthorized");
                await Clients.Caller.SendAsync("onError", _localizer["ErrorNotAuthorizedRoom"].Value);
                return;
            }
            var room = await _rooms.GetByNameAsync(currentRoom);
            if (room == null)
            {
                _logger.LogWarning("SendMessage room missing user={User} room={Room}", IdentityName, currentRoom);
                activity?.SetStatus(ActivityStatusCode.Error, "room_missing");
                await Clients.Caller.SendAsync("onError", _localizer["ErrorOccurred"].Value);
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
                msg = await _messages.CreateAsync(msg);
            }
            catch (System.Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "SendMessage persistence failed user={User} room={Room}", IdentityName, room.Name);
                await Clients.Caller.SendAsync("onError", _localizer["ErrorOccurred"].Value);
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
                CorrelationId = correlationId,
                ReadBy = (msg.ReadBy != null ? msg.ReadBy.ToArray() : Array.Empty<string>())
            };
            await Clients.Group(room.Name).SendAsync("newMessage", vm);
            try
            {
                _unreadScheduler?.Schedule(msg);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unread notification scheduling failed for message {Id} in room {Room}", msg.Id, room.Name);
            }
            _metrics.IncMessagesSent();
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        /// <summary>
        /// Marks a message as read by the current user and broadcasts an update to the room.
        /// Rate limited per user to prevent abuse and database saturation.
        /// </summary>
        public async Task MarkRead(int messageId)
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.MarkRead");
            activity?.SetTag("chat.messageId", messageId);
            
            // Check rate limit first
            if (!_markReadRateLimiter.TryAcquire(IdentityName))
            {
                _logger.LogWarning("MarkRead rate limit exceeded for user {User}, messageId={MessageId}", IdentityName, messageId);
                _metrics.IncMarkReadRateLimitViolation(IdentityName);
                activity?.SetStatus(ActivityStatusCode.Error, "rate_limit_exceeded");
                await Clients.Caller.SendAsync("onError", _localizer["ErrorRateLimitExceeded"].Value);
                return;
            }
            
            // Get user and room from Context.Items (per-connection state)
            var user = Context.Items["UserProfile"] as UserViewModel;
            var currentRoom = Context.Items["CurrentRoom"] as string;
            
            if (user == null || string.IsNullOrEmpty(currentRoom)) return;
            
            var updated = await _messages.MarkReadAsync(messageId, IdentityName);
            if (updated == null) return;
            
            // Cancel any pending unread notification for this message since someone has read it
            try
            {
                _unreadScheduler?.CancelNotification(messageId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel unread notification for message {Id}", messageId);
            }
            
            // Use the message's actual room name for broadcasting
            var messageRoom = updated.ToRoom?.Name;
            if (string.IsNullOrEmpty(messageRoom)) return;
            // Optionally, guard if the message's room does not match the user's current room
            try
            {
                await Clients.Group(messageRoom).SendAsync("messageRead", new { id = messageId, readers = updated.ReadBy?.ToArray() ?? Array.Empty<string>() });
            }
            catch { /* ignore broadcast errors */ }
        }

        /// <summary>
        /// Returns backend health status (Cosmos DB, Redis connectivity).
        /// Used by clients to detect when SignalR connection is active but backend services are unreachable.
        /// </summary>
        public async Task<object> GetHealthStatus()
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.GetHealthStatus");
            
            try
            {
                // Create cancellation token with 5-second timeout
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                
                // Run health checks tagged as "ready" (Redis + Cosmos)
                var healthReport = await _healthCheckService.CheckHealthAsync(
                    (check) => check.Tags.Contains("ready"),
                    cts.Token
                );
                
                var timestamp = DateTime.UtcNow;
                var isHealthy = healthReport.Status == HealthStatus.Healthy;
                
                // Extract individual component statuses
                var redisStatus = healthReport.Entries.TryGetValue("redis", out var redisEntry)
                    ? redisEntry.Status.ToString()
                    : "Unknown";
                var cosmosStatus = healthReport.Entries.TryGetValue("cosmos", out var cosmosEntry)
                    ? cosmosEntry.Status.ToString()
                    : "Unknown";
                
                activity?.SetTag("health.overall", isHealthy);
                activity?.SetTag("health.redis", redisStatus);
                activity?.SetTag("health.cosmos", cosmosStatus);
                
                _logger.LogDebug(
                    "Health check for user {User}: Overall={Overall}, Redis={Redis}, Cosmos={Cosmos}",
                    IdentityName, isHealthy, redisStatus, cosmosStatus
                );
                
                return new
                {
                    isHealthy,
                    redis = redisStatus,
                    cosmos = cosmosStatus,
                    timestamp
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetHealthStatus failed for user {User}", IdentityName);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                
                return new
                {
                    isHealthy = false,
                    redis = "Error",
                    cosmos = "Error",
                    timestamp = DateTime.UtcNow,
                    error = ex.Message
                };
            }
        }

        /// <summary>
        /// Heartbeat to detect stale connections when disconnect events are lost.
        /// Updates last activity timestamp in Redis and returns health status.
        /// Client should invoke every 30 seconds.
        /// </summary>
        public async Task<object> Heartbeat()
        {
            using var activity = Tracing.ActivitySource.StartActivity("ChatHub.Heartbeat");
            activity?.SetTag("chat.user", IdentityName);
            
            try
            {
                // Update last activity timestamp in Redis (2-minute TTL)
                await _presenceTracker.UpdateHeartbeatAsync(IdentityName);
                
                _logger.LogDebug("Heartbeat from user {User}", IdentityName);
                
                // Return health status with heartbeat acknowledgment
                var healthStatus = await GetHealthStatus();
                
                return new
                {
                    acknowledged = true,
                    timestamp = DateTime.UtcNow,
                    health = healthStatus
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed for user {User}", IdentityName);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                
                return new
                {
                    acknowledged = false,
                    timestamp = DateTime.UtcNow,
                    error = ex.Message
                };
            }
        }
    }
}
