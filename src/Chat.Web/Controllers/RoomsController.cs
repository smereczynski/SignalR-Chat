using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Repositories;
using Chat.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Chat.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Chat.Web.ViewModels;
using System.Text.Json;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    /// <summary>
    /// Provides room listing. Dynamic creation / deletion have been deprecated in favor of static, predefined rooms with fixed membership.
    /// Legacy POST/DELETE endpoints now return 410 (Gone).
    /// </summary>
    public class RoomsController : ControllerBase
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IHubContext<ChatHub> _hubContext;

        /// <summary>
        /// DI constructor for rooms API.
        /// </summary>
        public RoomsController(IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext)
        {
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
        }

    /// <summary>
    /// Returns rooms the authenticated user is authorized to see.
    /// Authorization model: user profile may define FixedRooms (whitelist). If none defined, returns empty list.
    /// This ensures clients cannot discover rooms they are not assigned to.
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<RoomViewModel>> Get()
        {
            // Retrieve user profile
            var userName = User?.Identity?.Name;
            var profile = string.IsNullOrWhiteSpace(userName) ? null : _users.GetByUserName(userName);
            var allowed = profile?.FixedRooms ?? new List<string>();

            var rooms = _rooms.GetAll()
                .Where(r => allowed.Contains(r.Name))
                .Select(r => new RoomViewModel { Id = r.Id, Name = r.Name, Admin = r.Admin?.UserName })
                .ToList();

            // Manual serialization (workaround for test server PipeWriter issue) + camelCase for client expectations.
            var json = JsonSerializer.Serialize(rooms, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }

    /// <summary>
    /// Returns a single room by id.
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<Room> Get(int id)
        {
            var room = _rooms.GetById(id);
            if (room == null)
                return NotFound();

            var vm = new RoomViewModel { Id = room.Id, Name = room.Name, Admin = room.Admin?.UserName };
            return Ok(vm);
        }

    /// <summary>
    /// Deprecated: dynamic creation removed. Returns 410 Gone.
    /// </summary>
    [HttpPost]
    public Task<ActionResult> Create(RoomViewModel viewModel)
        => Task.FromResult<ActionResult>(StatusCode(410, "Dynamic room creation disabled (static rooms only)"));

    // NOTE: Rename (PUT) endpoint intentionally removed as part of feature deprecation.

    /// <summary>
    /// Deprecated: dynamic deletion removed. Returns 410 Gone.
    /// </summary>
    [HttpDelete("{id}")]
    public Task<ActionResult> Delete(int id)
        => Task.FromResult<ActionResult>(StatusCode(410, "Dynamic room deletion disabled (static rooms only)"));
    }
}
