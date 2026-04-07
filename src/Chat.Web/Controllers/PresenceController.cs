using System;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Chat.Web.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PresenceController : ControllerBase
    {
        private readonly IUsersRepository _users;
        private readonly IRoomsRepository _rooms;
        private readonly IPresenceTracker _presenceTracker;
        private readonly ILogger<PresenceController> _logger;

        public PresenceController(
            IUsersRepository users,
            IRoomsRepository rooms,
            IPresenceTracker presenceTracker,
            ILogger<PresenceController> logger)
        {
            _users = users;
            _rooms = rooms;
            _presenceTracker = presenceTracker;
            _logger = logger;
        }

        public record PresencePingDto(string? RoomName);

        /// <summary>
        /// Returns a snapshot of current room presence: roomName -> user count and user list.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var allUsers = await _presenceTracker.GetAllUsersAsync();
            var snapshot = allUsers
                .Where(u => !string.IsNullOrWhiteSpace(u.CurrentRoom))
                .GroupBy(u => u.CurrentRoom)
                .Select(g => new {
                    room = g.Key,
                    count = g.Count(),
                    users = g.Select(x => new { x.UserName, x.FullName })
                })
                .OrderBy(x => x.room)
                .ToList();
            return Ok(snapshot);
        }

        [HttpPost("ping")]
        public async Task<IActionResult> Ping([FromBody] PresencePingDto? dto)
        {
            var identityName = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(identityName))
            {
                return Unauthorized();
            }

            var profile = await _users.GetByUserNameAsync(identityName).ConfigureAwait(false);
            if (profile == null || !profile.Enabled)
            {
                return Forbid();
            }

            var requestedRoom = dto?.RoomName?.Trim();
            var room = string.IsNullOrWhiteSpace(requestedRoom)
                ? null
                : await _rooms.GetByNameAsync(requestedRoom).ConfigureAwait(false);
            var resolvedRoom = room != null &&
                               RoomAccessPolicy.CanAccessRoom(profile, room)
                ? requestedRoom
                : string.Empty;

            var canonicalUserName = string.IsNullOrWhiteSpace(profile.UserName)
                ? identityName
                : profile.UserName;

            await _presenceTracker.SetUserRoomAsync(
                canonicalUserName,
                string.IsNullOrWhiteSpace(profile.FullName) ? canonicalUserName : profile.FullName,
                profile.Avatar ?? string.Empty,
                resolvedRoom).ConfigureAwait(false);

            await _presenceTracker.UpdateHeartbeatAsync(canonicalUserName).ConfigureAwait(false);

            _logger.LogDebug(
                "Presence ping accepted: user={User} room={Room}",
                LogSanitizer.Sanitize(canonicalUserName),
                LogSanitizer.Sanitize(resolvedRoom));

            return Accepted();
        }

        [HttpPost("leave")]
        public async Task<IActionResult> Leave()
        {
            var identityName = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(identityName))
            {
                return Unauthorized();
            }

            var profile = await _users.GetByUserNameAsync(identityName).ConfigureAwait(false);
            var canonicalUserName = string.IsNullOrWhiteSpace(profile?.UserName) ? identityName : profile.UserName;

            await _presenceTracker.RemoveUserAsync(canonicalUserName).ConfigureAwait(false);

            _logger.LogDebug(
                "Presence leave accepted: user={User}",
                LogSanitizer.Sanitize(canonicalUserName));

            return Accepted();
        }
    }
}
