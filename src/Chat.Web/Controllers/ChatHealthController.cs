using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Hubs;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/health/chat")]
    [ApiController]
    public class ChatHealthController : ControllerBase
    {
        [HttpGet("presence")]
        public IActionResult Presence()
        {
            var snapshot = ChatHub.Snapshot();
            var rooms = snapshot.GroupBy(u => u.CurrentRoom)
                .Select(g => new { room = string.IsNullOrEmpty(g.Key) ? "" : g.Key, users = g.Select(x => new { x.UserName }).ToList(), count = g.Count() })
                .OrderBy(r => r.room)
                .ToList();
            return Ok(new { total = snapshot.Count, rooms });
        }
    }
}
