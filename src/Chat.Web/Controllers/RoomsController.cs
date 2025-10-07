using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Repositories;
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
    /// Provides read-only room listing (static predefined rooms). Dynamic creation, update, and deletion are not part of this application.
    /// </summary>
    public class RoomsController : ControllerBase
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IHubContext<ChatHub> _hubContext;

        public RoomsController(IRoomsRepository rooms, IUsersRepository users, IHubContext<ChatHub> hubContext)
        {
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Returns rooms the authenticated user is authorized to see (intersection with user FixedRooms whitelist).
        /// </summary>
        [HttpGet]
        public ActionResult<IEnumerable<RoomViewModel>> Get()
        {
            var userName = User?.Identity?.Name;
            var profile = string.IsNullOrWhiteSpace(userName) ? null : _users.GetByUserName(userName);
            var allowed = profile?.FixedRooms ?? new List<string>();

            var rooms = _rooms.GetAll()
                .Where(r => allowed.Contains(r.Name))
                .Select(r => new RoomViewModel { Id = r.Id, Name = r.Name, Admin = r.Admin?.UserName })
                .ToList();

            var json = JsonSerializer.Serialize(rooms, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
        }

        /// <summary>
        /// Returns a single room by id (only if in user whitelist).
        /// </summary>
        [HttpGet("{id}")]
        public ActionResult<RoomViewModel> Get(int id)
        {
            var userName = User?.Identity?.Name;
            var profile = string.IsNullOrWhiteSpace(userName) ? null : _users.GetByUserName(userName);
            var allowed = profile?.FixedRooms ?? new List<string>();

            var room = _rooms.GetById(id);
            if (room == null || !allowed.Contains(room.Name))
                return NotFound();

            var vm = new RoomViewModel { Id = room.Id, Name = room.Name, Admin = room.Admin?.UserName };
            return Ok(vm);
        }
    }
}
