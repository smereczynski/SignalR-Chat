using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Hubs;
using System.Linq;

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PresenceController : ControllerBase
    {
        /// <summary>
        /// Returns a snapshot of current room presence: roomName -> user count and user list.
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            var snapshot = ChatHub.Snapshot()
                .GroupBy(u => u.CurrentRoom)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => new {
                    room = g.Key,
                    count = g.Count(),
                    users = g.Select(x => new { x.UserName, x.FullName, x.Device })
                })
                .OrderBy(x => x.room)
                .ToList();
            return Ok(snapshot);
        }
    }
}
