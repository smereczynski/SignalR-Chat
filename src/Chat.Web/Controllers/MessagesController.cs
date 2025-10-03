using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Repositories;
using Chat.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Chat.Web.Hubs;
using Chat.Web.ViewModels;
using System.Text.RegularExpressions;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly IMessagesRepository _messages;
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IHubContext<ChatHub> _hubContext;

        public MessagesController(IMessagesRepository messages,
            IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext)
        {
            _messages = messages;
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
        }

        [HttpGet("{id}")]
        public ActionResult<Room> Get(int id)
        {
            var message = _messages.GetById(id);
            if (message == null)
                return NotFound();

            var vm = new MessageViewModel
            {
                Id = message.Id,
                Content = message.Content,
                FromUserName = message.FromUser?.UserName,
                FromFullName = message.FromUser?.FullName,
                Avatar = message.FromUser?.Avatar,
                Room = message.ToRoom?.Name,
                Timestamp = message.Timestamp
            };
            return Ok(vm);
        }

        [HttpGet("Room/{roomName}")]
        public IActionResult GetMessages(string roomName)
        {
            var room = _rooms.GetByName(roomName);
            if (room == null)
                return BadRequest();

            var items = _messages.GetRecentByRoom(room.Name, 20)
                .Select(m => new MessageViewModel
                {
                    Id = m.Id,
                    Content = m.Content,
                    FromUserName = m.FromUser?.UserName,
                    FromFullName = m.FromUser?.FullName,
                    Avatar = m.FromUser?.Avatar,
                    Room = room.Name,
                    Timestamp = m.Timestamp
                });
            return Ok(items);
        }

        [HttpPost]
        public async Task<ActionResult<Message>> Create(MessageViewModel viewModel)
        {
            var user = _users.GetByUserName(User.Identity.Name);
            var room = _rooms.GetByName(viewModel.Room);
            if (room == null)
                return BadRequest();

            var msg = new Message()
            {
                Content = Regex.Replace(viewModel.Content, @"<.*?>", string.Empty),
                FromUser = user,
                ToRoom = room,
                Timestamp = DateTime.Now
            };

            msg = _messages.Create(msg);

            // Broadcast the message
            var createdMessage = new MessageViewModel
            {
                Id = msg.Id,
                Content = msg.Content,
                FromUserName = msg.FromUser?.UserName,
                FromFullName = msg.FromUser?.FullName,
                Avatar = msg.FromUser?.Avatar,
                Room = room.Name,
                Timestamp = msg.Timestamp
            };
            await _hubContext.Clients.Group(room.Name).SendAsync("newMessage", createdMessage);

            return CreatedAtAction(nameof(Get), new { id = msg.Id }, createdMessage);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var message = _messages.GetById(id);
            if (message?.FromUser?.UserName != User.Identity.Name)
                message = null;

            if (message == null)
                return NotFound();

            _messages.Delete(id, User.Identity.Name);

            await _hubContext.Clients.All.SendAsync("removeChatMessage", message.Id);

            return Ok();
        }
    }
}
