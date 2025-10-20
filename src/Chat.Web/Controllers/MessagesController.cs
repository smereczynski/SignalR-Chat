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
    /// REST endpoints for querying chat messages. A lightweight POST is (re)introduced primarily for
    /// integration tests and extremely early user interactions (the race right after authentication
    /// before the SignalR hub is fully ready). The authoritative/normal realtime path remains the hub.
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
                Timestamp = message.Timestamp,
                ReadBy = message.ReadBy != null ? message.ReadBy.ToArray() : Array.Empty<string>()
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
                Timestamp = m.Timestamp,
                ReadBy = m.ReadBy != null ? m.ReadBy.ToArray() : Array.Empty<string>()
            });
            if (UseManualSerialization)
            {
                return ManualJson(items);
            }
            return Ok(items);
        }

        /// <summary>
        /// Create a message in a room (fallback path used by tests / immediate post after auth race mitigation).
        /// Still broadcasts over the hub for consistency with realtime clients.
        /// </summary>
        public class CreateMessageDto
        {
            public string Room { get; set; }
            public string Content { get; set; }
            public string CorrelationId { get; set; }
        }

        [HttpPost]
        public IActionResult Post([FromBody] CreateMessageDto dto)
        {
            // Feature flag: disable REST creation path unless explicitly enabled (tests / emergency fallback)
            var enabled = string.Equals(_configuration["Features:EnableRestPostMessages"], "true", StringComparison.OrdinalIgnoreCase);
            if (!enabled)
            {
                return NotFound(); // Pretend endpoint absent in production
            }
            if (dto == null || string.IsNullOrWhiteSpace(dto.Room) || string.IsNullOrWhiteSpace(dto.Content))
                return BadRequest();

            var room = _rooms.GetByName(dto.Room);
            if (room == null)
                return NotFound();

            // Basic authz: ensure user profile allows this room when FixedRooms is defined.
            var user = _users.GetByUserName(User?.Identity?.Name);
            if (user?.FixedRooms != null && user.FixedRooms.Any() && !user.FixedRooms.Contains(room.Name))
            {
                return Forbid();
            }

            // Sanitize (strip tags) similar to hub path.
            var sanitized = Regex.Replace(dto.Content, @"<.*?>", string.Empty);
            var message = new Message
            {
                Content = sanitized,
                FromUser = user,
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            };
            try
            {
                message = _messages.Create(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "REST message create failed user={User} room={Room}", User?.Identity?.Name, room.Name);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            var vm = new MessageViewModel
            {
                Id = message.Id,
                Content = message.Content,
                FromUserName = message.FromUser?.UserName,
                FromFullName = message.FromUser?.FullName,
                Avatar = message.FromUser?.Avatar,
                Room = room.Name,
                Timestamp = message.Timestamp,
                CorrelationId = dto.CorrelationId,
                ReadBy = message.ReadBy != null ? message.ReadBy.ToArray() : Array.Empty<string>()
            };

            // Fire-and-forget hub broadcast (do not block API latency on network fan-out)
            _ = _hubContext.Clients.Group(room.Name).SendAsync("newMessage", vm);
            _metrics.IncMessagesSent();

            if (UseManualSerialization)
            {
                return ManualJson(vm, StatusCodes.Status201Created, $"/api/Messages/{vm.Id}");
            }
            return Created($"/api/Messages/{vm.Id}", vm);
        }

        /// <summary>
        /// Mark a message as read for the current user. Broadcasts update via hub.
        /// </summary>
        [HttpPost("{id}/read")]
        public IActionResult MarkRead(int id)
        {
            var updated = _messages.MarkRead(id, User?.Identity?.Name);
            if (updated == null) return NotFound();
            // Fire-and-forget broadcast of readers list to the room
            _ = _hubContext.Clients.Group(updated.ToRoom?.Name ?? string.Empty)
                .SendAsync("messageRead", new { id = updated.Id, readers = updated.ReadBy?.ToArray() ?? Array.Empty<string>() });
            return NoContent();
        }
    }
}
