using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Services;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/health/chat")]
    [ApiController]
    public class ChatHealthController : ControllerBase
    {
        private readonly IPresenceTracker _presenceTracker;

        public ChatHealthController(IPresenceTracker presenceTracker)
        {
            _presenceTracker = presenceTracker;
        }

        [HttpGet("presence")]
        public async Task<IActionResult> Presence()
        {
            var snapshot = await _presenceTracker.GetAllUsersAsync();
            var rooms = snapshot.GroupBy(u => u.CurrentRoom)
                .Select(g => new { room = string.IsNullOrEmpty(g.Key) ? "" : g.Key, users = g.Select(x => new { x.UserName }).ToList(), count = g.Count() })
                .OrderBy(r => r.room)
                .ToList();
            return Ok(new { total = snapshot.Count, rooms });
        }
    }
}
