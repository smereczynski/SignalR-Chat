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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Chat.Web.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    /// <summary>
    /// REST endpoints for querying chat messages (GET only). All message creation occurs exclusively via the SignalR hub.
    /// </summary>
    public class MessagesController : ControllerBase
    {
        private readonly IMessagesRepository _messages;
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly Services.IInProcessMetrics _metrics;
        private readonly ILogger<MessagesController> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// DI constructor for messages API.
        /// </summary>
        public MessagesController(IMessagesRepository messages,
            IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext,
            Services.IInProcessMetrics metrics,
            ILogger<MessagesController> logger,
            IConfiguration configuration)
        {
            _messages = messages;
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
            _metrics = metrics;
            _logger = logger;
            _configuration = configuration;
        }

        private bool UseManualSerialization => string.Equals(_configuration["Testing:InMemory"], "true", StringComparison.OrdinalIgnoreCase);

        private ContentResult ManualJson(object obj, int statusCode = StatusCodes.Status200OK, string location = null)
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            if (!string.IsNullOrEmpty(location))
            {
                Response.Headers["Location"] = location;
            }
            Response.StatusCode = statusCode;
            return Content(json, "application/json");
        }

        /// <summary>
        /// Retrieve a single message by id.
        /// </summary>
        [HttpGet("{id}")]
        public ActionResult<MessageViewModel> Get(int id)
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
            if (UseManualSerialization)
            {
                return ManualJson(items);
            }
            return Ok(items);
        }

        // Note: POST creation removed. All message creation flows through SignalR hub methods.
    }
}
