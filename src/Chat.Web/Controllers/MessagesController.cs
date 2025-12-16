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
        private readonly ILogger<MessagesController> _logger;
        private readonly IConfiguration _configuration;
        private readonly Services.ITranslationJobQueue _translationQueue;
        private readonly Microsoft.Extensions.Options.IOptions<Options.TranslationOptions> _translationOptions;

        /// <summary>
        /// DI constructor for messages API.
        /// </summary>
        public MessagesController(IMessagesRepository messages,
            IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext,
            ILogger<MessagesController> logger,
            Services.ITranslationJobQueue translationQueue,
            Microsoft.Extensions.Options.IOptions<Options.TranslationOptions> translationOptions)
        {
            _messages = messages;
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
            _logger = logger;
            _configuration = null; // Removed IConfiguration dependency
            _translationQueue = translationQueue;
            _translationOptions = translationOptions;
        }

        private bool UseManualSerialization => false; // Always false - use normal JSON serialization

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
        public async Task<ActionResult<MessageViewModel>> Get(int id)
        {
            var message = await _messages.GetByIdAsync(id);
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
        public async Task<IActionResult> GetMessages(string roomName, [FromQuery] DateTime? before = null, [FromQuery] int take = 20)
        {
            if (take <= 0) take = 1;
            if (take > 100) take = 100; // cap
            var room = await _rooms.GetByNameAsync(roomName);
            if (room == null)
                return BadRequest();

            IEnumerable<Message> source = before.HasValue
                ? await _messages.GetBeforeByRoomAsync(room.Name, before.Value, take)
                : await _messages.GetRecentByRoomAsync(room.Name, take);

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
        public async Task<IActionResult> Post([FromBody] CreateMessageDto dto,
            [FromServices] Services.IInProcessMetrics metrics,
            [FromServices] Services.UnreadNotificationScheduler unreadScheduler)
        {
            // Feature flag: disable REST creation path unless explicitly enabled (tests / emergency fallback)
            var enabled = _configuration != null && string.Equals(_configuration["Features:EnableRestPostMessages"], "true", StringComparison.OrdinalIgnoreCase);
            if (!enabled)
            {
                return NotFound(); // Pretend endpoint absent in production
            }
            if (dto == null || string.IsNullOrWhiteSpace(dto.Room) || string.IsNullOrWhiteSpace(dto.Content))
                return BadRequest();

            var room = await _rooms.GetByNameAsync(dto.Room);
            if (room == null)
                return NotFound();

            // Basic authz: ensure user profile allows this room when FixedRooms is defined.
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
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
                message = await _messages.CreateAsync(message);
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
            try
            {
                unreadScheduler?.Schedule(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unread notification scheduling failed for message {Id} in room {Room}", message.Id, room.Name);
            }
            metrics?.IncMessagesSent();

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
        public async Task<IActionResult> MarkRead(int id)
        {
            var updated = await _messages.MarkReadAsync(id, User?.Identity?.Name);
            if (updated == null) return NotFound();
            // Fire-and-forget broadcast of readers list to the room
            _ = _hubContext.Clients.Group(updated.ToRoom?.Name ?? string.Empty)
                .SendAsync("messageRead", new { id = updated.Id, readers = updated.ReadBy?.ToArray() ?? Array.Empty<string>() });
            return NoContent();
        }

        /// <summary>
        /// Manually retry translation for a failed message. Re-queues with high priority.
        /// Requires translation feature to be enabled and message to be in Failed state.
        /// </summary>
        [HttpPost("{id}/retry-translation")]
        public async Task<IActionResult> RetryTranslation(int id)
        {
            using var activity = Observability.Tracing.ActivitySource.StartActivity("api.messages.retry-translation");
            activity?.SetTag("message.id", id);
            
            // Check if translation is enabled
            if (_translationOptions?.Value?.Enabled != true || _translationQueue == null)
            {
                return BadRequest(new { error = "Translation feature is not enabled" });
            }
            
            var message = await _messages.GetByIdAsync(id);
            if (message == null)
                return NotFound(new { error = "Message not found" });
            
            // Authorization: user must be in the same room
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            if (user?.FixedRooms != null && user.FixedRooms.Any() && !user.FixedRooms.Contains(message.ToRoom?.Name))
            {
                return Forbid();
            }
            
            if (message.TranslationStatus != TranslationStatus.Failed)
                return BadRequest(new { error = "Translation is not in failed state" });

            var senderProfile = await _users.GetByUserNameAsync(message.FromUser?.UserName);
            var sourceLanguage = Chat.Web.Utilities.LanguageCode.NormalizeToLanguageCode(senderProfile?.PreferredLanguage) ?? "auto";

            var room = await _rooms.GetByNameAsync(message.ToRoom?.Name);
            var targets = Chat.Web.Utilities.LanguageCode.BuildTargetLanguages(room?.Languages, sourceLanguage);
            
            // Create new job with high priority
            var job = new MessageTranslationJob
            {
                MessageId = message.Id,
                RoomName = message.ToRoom.Name,
                Content = message.Content,
                SourceLanguage = sourceLanguage,
                TargetLanguages = targets,
                DeploymentName = _translationOptions.Value.DeploymentName,
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0,
                Priority = 10, // High priority for manual retries
                JobId = $"transjob:{message.Id}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
            
            await _translationQueue.RequeueAsync(job, highPriority: true);
            await _messages.UpdateTranslationAsync(message.Id, TranslationStatus.Pending, new Dictionary<string, string>(), job.JobId);
            
            _logger.LogInformation("Manual retry triggered for message {MessageId} by user {User}", id, User.Identity.Name);
            
            // Broadcast status update to room
            _ = _hubContext.Clients.Group(message.ToRoom.Name)
                .SendAsync("translationRetrying", new
                {
                    messageId = id,
                    status = "Pending",
                    timestamp = DateTime.UtcNow
                });
            
            if (UseManualSerialization)
            {
                return ManualJson(new { success = true, jobId = job.JobId });
            }
            return Ok(new { success = true, jobId = job.JobId });
        }
    }
}
