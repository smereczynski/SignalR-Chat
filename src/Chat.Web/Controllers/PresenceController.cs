using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Services;
using System.Linq;
using System.Threading.Tasks;

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PresenceController : ControllerBase
    {
        private readonly IPresenceTracker _presenceTracker;

        public PresenceController(IPresenceTracker presenceTracker)
        {
            _presenceTracker = presenceTracker;
        }

        /// <summary>
        /// Returns a snapshot of current room presence: roomName -> user count and user list.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var allUsers = await _presenceTracker.GetAllUsersAsync();
            var snapshot = allUsers
                .GroupBy(u => u.CurrentRoom)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => new {
                    room = g.Key,
                    count = g.Count(),
                    users = g.Select(x => new { x.UserName, x.FullName })
                })
                .OrderBy(x => x.room)
                .ToList();
            return Ok(snapshot);
        }
    }
}

