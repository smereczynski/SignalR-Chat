using System;
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
    /// Provides read-only derived dispatch-center pair room listing.
    /// Pair rooms are synchronized from dispatch-center topology and are never managed directly by end users.
    /// </summary>
    public class RoomsController : ControllerBase
    {
        private const string EmptyStateReasonHeader = "X-Chat-Empty-State-Reason";

        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IDispatchCentersRepository _dispatchCenters;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IPresenceTracker _presenceTracker;
        private readonly ILogger<RoomsController> _logger;

        public RoomsController(
            IRoomsRepository rooms,
            IUsersRepository users,
            IDispatchCentersRepository dispatchCenters,
            IHubContext<ChatHub> hubContext,
            IPresenceTracker presenceTracker,
            ILogger<RoomsController> logger)
        {
            _rooms = rooms;
            _users = users;
            _dispatchCenters = dispatchCenters;
            _hubContext = hubContext;
            _presenceTracker = presenceTracker;
            _logger = logger;
        }

        /// <summary>
        /// Returns rooms the authenticated user is authorized to see based on dispatch-center pair rooms.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RoomViewModel>>> Get()
        {
            var userName = User?.Identity?.Name;
            var profile = string.IsNullOrWhiteSpace(userName) ? null : await _users.GetByUserNameAsync(userName);
            if (profile == null)
            {
                Response.Headers[EmptyStateReasonHeader] = "profile-not-found";
                return Ok(Array.Empty<RoomViewModel>());
            }

            var allRooms = (await _rooms.GetAllAsync()).ToList();
            var rooms = RoomAccessPolicy.GetAccessibleRooms(profile, allRooms)
                .Select(r => new RoomViewModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    DisplayName = r.DisplayName,
                    PairKey = r.PairKey,
                    DispatchCenterAId = r.DispatchCenterAId,
                    DispatchCenterBId = r.DispatchCenterBId,
                    IsActive = r.IsActive,
                    Languages = r.Languages
                })
                .ToList();

            if (rooms.Count == 0)
            {
                Response.Headers[EmptyStateReasonHeader] = await DetermineEmptyStateReasonAsync(profile, allRooms).ConfigureAwait(false);
            }

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
            var room = await _rooms.GetByIdAsync(id);
            if (!RoomAccessPolicy.CanAccessRoom(profile, room))
                return NotFound();

            var vm = new RoomViewModel
            {
                Id = room.Id,
                Name = room.Name,
                DisplayName = room.DisplayName,
                PairKey = room.PairKey,
                DispatchCenterAId = room.DispatchCenterAId,
                DispatchCenterBId = room.DispatchCenterBId,
                IsActive = room.IsActive,
                Languages = room.Languages
            };
            return Ok(vm);
        }

        /// <summary>
        /// Returns all users assigned to a room with live presence indicator.
        /// </summary>
        [HttpGet("by-name/{roomName}/users")]
        public async Task<IActionResult> GetAssignedUsersWithPresence(string roomName)
        {
            var safeRoomNameForLog = roomName?.Replace("\r", string.Empty).Replace("\n", string.Empty);
            _logger.LogDebug("Room users presence query: room={RoomName}", safeRoomNameForLog);
            if (string.IsNullOrWhiteSpace(roomName))
            {
                return BadRequest();
            }

            var currentUserName = User?.Identity?.Name;
            var currentProfile = string.IsNullOrWhiteSpace(currentUserName) ? null : await _users.GetByUserNameAsync(currentUserName);
            var room = await _rooms.GetByNameAsync(roomName);
            if (!RoomAccessPolicy.CanAccessRoom(currentProfile, room))
            {
                return Forbid();
            }

            var users = RoomAccessPolicy.GetAssignedUsersForRoom(room, await _users.GetAllAsync())
                .OrderBy(u => u.FullName ?? u.UserName)
                .ToList();

            var presence = await _presenceTracker.GetAllUsersAsync();
            var activeHeartbeats = await _presenceTracker.GetActiveHeartbeatsAsync();
            var activeHeartbeatsSet = new HashSet<string>(activeHeartbeats, System.StringComparer.OrdinalIgnoreCase);

            var onlineUsers = new HashSet<string>(
                presence
                    .Select(p => p.UserName)
                    .Where(u => activeHeartbeatsSet.Contains(u)),
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
                "Room users presence query: room={RoomName} assigned={AssignedCount} online={OnlineCount} activeHeartbeats={HeartbeatCount}",
                safeRoomNameForLog,
                result.Count,
                onlineCount,
                activeHeartbeats.Count);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }

        private async Task<string> DetermineEmptyStateReasonAsync(Models.ApplicationUser profile, List<Models.Room> allRooms)
        {
            if (profile == null)
            {
                return "profile-not-found";
            }

            if (!profile.Enabled)
            {
                return "user-disabled";
            }

            if (string.IsNullOrWhiteSpace(profile.DispatchCenterId))
            {
                return "no-dispatch-center";
            }

            var dispatchCenter = await _dispatchCenters.GetByIdAsync(profile.DispatchCenterId).ConfigureAwait(false);
            var correspondingIds = dispatchCenter?.CorrespondingDispatchCenterIds?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            if (correspondingIds.Count == 0)
            {
                return "no-corresponding-dispatch-centers";
            }

            var relatedPairRooms = allRooms
                .Where(room => RoomAccessPolicy.IsDispatchCenterScoped(room) &&
                               DispatchCenterPairing.IncludesDispatchCenter(room, profile.DispatchCenterId))
                .ToList();

            if (relatedPairRooms.Count == 0)
            {
                return "no-derived-pair-rooms";
            }

            if (relatedPairRooms.Any(room => !room.IsActive))
            {
                return "inactive-pair-room";
            }

            return "no-accessible-rooms";
        }
    }
}
