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
    /// <summary>
    /// REST endpoints for querying and creating chat messages.
    /// GET endpoints support pagination by timestamp; POST broadcasts to a SignalR room and increments metrics.
    /// </summary>
    public class MessagesController : ControllerBase
    {
        private readonly IMessagesRepository _messages;
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly Services.IInProcessMetrics _metrics;

        /// <summary>
        /// DI constructor for messages API.
        /// </summary>
        public MessagesController(IMessagesRepository messages,
            IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext,
            Services.IInProcessMetrics metrics)
        {
            _messages = messages;
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
            _metrics = metrics;
        }

    /// <summary>
    /// Retrieve a single message by id.
    /// </summary>
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

    /// <summary>
    /// Get recent messages for a room. Optionally page backwards using a 'before' timestamp.
    /// </summary>
    [HttpGet("Room/{roomName}")]
    public IActionResult GetMessages(string roomName, [FromQuery] DateTime? before = null, [FromQuery] int take = 20)
        {
            if (take <= 0) take = 1;
            if (take > 100) take = 100; // cap
            var room = _rooms.GetByName(roomName);
            if (room == null)
                return BadRequest();

            IEnumerable<Message> source = before.HasValue
                ? _messages.GetBeforeByRoom(room.Name, before.Value, take)
                : _messages.GetRecentByRoom(room.Name, take);

            var items = source.Select(m => new MessageViewModel
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

    /// <summary>
    /// Create and broadcast a message to all clients in the target room.
    /// </summary>
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
                Timestamp = msg.Timestamp,
                CorrelationId = viewModel.CorrelationId
            };
            await _hubContext.Clients.Group(room.Name).SendAsync("newMessage", createdMessage);
            _metrics.IncMessagesSent();

            return CreatedAtAction(nameof(Get), new { id = msg.Id }, createdMessage);
        }

    }
}
