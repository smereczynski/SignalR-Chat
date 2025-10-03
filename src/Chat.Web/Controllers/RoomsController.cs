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

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RoomsController : ControllerBase
    {
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IHubContext<ChatHub> _hubContext;

        public RoomsController(IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext)
        {
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
        }

        [HttpGet]
        public ActionResult<IEnumerable<RoomViewModel>> Get()
        {
            var rooms = _rooms.GetAll()
                .Select(r => new RoomViewModel { Id = r.Id, Name = r.Name, Admin = r.Admin?.UserName });
            return Ok(rooms);
        }

        [HttpGet("{id}")]
        public ActionResult<Room> Get(int id)
        {
            var room = _rooms.GetById(id);
            if (room == null)
                return NotFound();

            var vm = new RoomViewModel { Id = room.Id, Name = room.Name, Admin = room.Admin?.UserName };
            return Ok(vm);
        }

        [HttpPost]
        public async Task<ActionResult<Room>> Create(RoomViewModel viewModel)
        {
            if (_rooms.GetByName(viewModel.Name) != null)
                return BadRequest("Invalid room name or room already exists");

            var user = _users.GetByUserName(User.Identity.Name);
            var room = new Room()
            {
                Name = viewModel.Name,
                Admin = user
            };

            room = _rooms.Create(room);

            var createdRoom = new RoomViewModel { Id = room.Id, Name = room.Name, Admin = room.Admin?.UserName };
            await _hubContext.Clients.All.SendAsync("addChatRoom", createdRoom);

            return CreatedAtAction(nameof(Get), new { id = room.Id }, createdRoom);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Edit(int id, RoomViewModel viewModel)
        {
            if (_rooms.GetByName(viewModel.Name) != null)
                return BadRequest("Invalid room name or room already exists");
            var room = _rooms.GetById(id);
            if (room?.Admin?.UserName != User.Identity.Name)
                room = null;

            if (room == null)
                return NotFound();

            room.Name = viewModel.Name;
            _rooms.Update(room);
            var updatedRoom = new RoomViewModel { Id = room.Id, Name = room.Name, Admin = room.Admin?.UserName };
            await _hubContext.Clients.All.SendAsync("updateChatRoom", updatedRoom);

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var room = _rooms.GetById(id);
            if (room?.Admin?.UserName != User.Identity.Name)
                room = null;

            if (room == null)
                return NotFound();

            _rooms.Delete(room.Id);

            await _hubContext.Clients.All.SendAsync("removeChatRoom", room.Id);
            await _hubContext.Clients.Group(room.Name).SendAsync("onRoomDeleted");

            return Ok();
        }
    }
}
