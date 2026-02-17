using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Chat.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Chat.Web.ViewModels;
using System.Text.Json;
using Chat.Web.Services;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    /// <summary>
    /// Provides read-only room listing (static predefined rooms). Dynamic creation, update, and deletion are not part of this application.
    /// </summary>
    public class RoomsController : ControllerBase
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IPresenceTracker _presenceTracker;
        private readonly ILogger<RoomsController> _logger;

        public RoomsController(
            IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext,
            IPresenceTracker presenceTracker,
            ILogger<RoomsController> logger)
        {
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
            _presenceTracker = presenceTracker;
            _logger = logger;
        }

        /// <summary>
        /// Returns rooms the authenticated user is authorized to see (intersection with user FixedRooms whitelist).
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RoomViewModel>>> Get()
        {
            var userName = User?.Identity?.Name;
            var profile = string.IsNullOrWhiteSpace(userName) ? null : await _users.GetByUserNameAsync(userName);
            var allowed = profile?.FixedRooms ?? [];
            var rooms = (await _rooms.GetAllAsync())
                .Where(r => allowed.Contains(r.Name))
                .Select(r => new RoomViewModel { Id = r.Id, Name = r.Name, Languages = r.Languages })
                .ToList();

            var json = JsonSerializer.Serialize(rooms, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }

        /// <summary>
        /// Returns a single room by id (only if in user whitelist).
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<RoomViewModel>> Get(int id)
        {
            var userName = User?.Identity?.Name;
            var profile = string.IsNullOrWhiteSpace(userName) ? null : await _users.GetByUserNameAsync(userName);
            var allowed = profile?.FixedRooms ?? new List<string>();

            var room = await _rooms.GetByIdAsync(id);
            if (room == null || !allowed.Contains(room.Name))
                return NotFound();

            var vm = new RoomViewModel { Id = room.Id, Name = room.Name, Languages = room.Languages };
            return Ok(vm);
        }

        /// <summary>
        /// Returns all users assigned to a room with live presence indicator.
        /// </summary>
        [HttpGet("by-name/{roomName}/users")]
        public async Task<IActionResult> GetAssignedUsersWithPresence(string roomName)
        {
            _logger.LogDebug("Room users presence query: room={RoomName}", roomName);
            if (string.IsNullOrWhiteSpace(roomName))
            {
                return BadRequest();
            }

            var currentUserName = User?.Identity?.Name;
            var currentProfile = string.IsNullOrWhiteSpace(currentUserName) ? null : await _users.GetByUserNameAsync(currentUserName);
            var allowed = currentProfile?.FixedRooms ?? new List<string>();

            if (!allowed.Any(r => string.Equals(r, roomName, System.StringComparison.OrdinalIgnoreCase)))
            {
                return Forbid();
            }

            var users = (await _users.GetAllAsync())
                .Where(u => u.Enabled)
                .Where(u => u.FixedRooms != null && u.FixedRooms.Any(r => string.Equals(r, roomName, System.StringComparison.OrdinalIgnoreCase)))
                .OrderBy(u => u.FullName ?? u.UserName)
                .ToList();

            var presence = await _presenceTracker.GetAllUsersAsync();
            // Presence is online/offline state persisted at connect/disconnect level (independent from current room join timing).
            var onlineUsers = new HashSet<string>(
                presence
                    .Select(p => p.UserName),
                System.StringComparer.OrdinalIgnoreCase);

            var result = users.Select(u => new
            {
                userName = u.UserName,
                fullName = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName,
                avatar = u.Avatar,
                isPresent = onlineUsers.Contains(u.UserName)
            }).ToList();

            var onlineCount = result.Count(u => u.isPresent);
            _logger.LogDebug(
                "Room users presence query: room={RoomName} assigned={AssignedCount} online={OnlineCount}",
                roomName,
                result.Count,
                onlineCount);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }
    }
}
