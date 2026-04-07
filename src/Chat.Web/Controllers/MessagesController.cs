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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Chat.Web.Services;
using Microsoft.Extensions.Options;

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
        private readonly EscalationService _escalations;

        /// <summary>
        /// DI constructor for messages API.
        /// </summary>
        public MessagesController(IMessagesRepository messages,
            IRoomsRepository rooms,
            IUsersRepository users,
            IHubContext<ChatHub> hubContext,
            ILogger<MessagesController> logger,
            EscalationService escalations)
        {
            _messages = messages;
            _rooms = rooms;
            _users = users;
            _hubContext = hubContext;
            _logger = logger;
            _escalations = escalations;
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
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            var room = await _rooms.GetByNameAsync(message.ToRoom?.Name);
            if (!RoomAccessPolicy.CanAccessRoom(user, room))
            {
                return Forbid();
            }

            message.ToRoom = room;

            var vm = new MessageViewModel
            {
                Id = message.Id,
                Content = message.Content,
                FromUserName = message.FromUser?.UserName,
                FromFullName = message.FromUser?.FullName,
                Avatar = message.FromUser?.Avatar,
                FromDispatchCenterId = message.FromDispatchCenterId,
                Room = message.ToRoom?.Name,
                Timestamp = message.Timestamp,
                ReadBy = message.ReadBy != null ? message.ReadBy.ToArray() : Array.Empty<string>(),
                ReadByDispatchCenterIds = message.ReadByDispatchCenterIds != null ? message.ReadByDispatchCenterIds.ToArray() : Array.Empty<string>(),
                EscalationStatus = message.EscalationStatus.ToString(),
                OpenEscalationId = message.OpenEscalationId,
                TranslationStatus = message.TranslationStatus.ToString(),
                SourceLanguage = Chat.Web.Utilities.LanguageCode.NormalizeToLanguageCode(message.FromUser?.PreferredLanguage, allowAuto: true) ?? "auto",
                Translations = message.Translations ?? new System.Collections.Generic.Dictionary<string, string>(),
                IsTranslated = message.IsTranslated
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
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            if (!RoomAccessPolicy.CanAccessRoom(user, room))
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
                FromDispatchCenterId = m.FromDispatchCenterId,
                Room = room.Name,
                Timestamp = m.Timestamp,
                ReadBy = m.ReadBy != null ? m.ReadBy.ToArray() : Array.Empty<string>(),
                ReadByDispatchCenterIds = m.ReadByDispatchCenterIds != null ? m.ReadByDispatchCenterIds.ToArray() : Array.Empty<string>(),
                EscalationStatus = m.EscalationStatus.ToString(),
                OpenEscalationId = m.OpenEscalationId,
                TranslationStatus = m.TranslationStatus.ToString(),
                SourceLanguage = Chat.Web.Utilities.LanguageCode.NormalizeToLanguageCode(m.FromUser?.PreferredLanguage, allowAuto: true) ?? "auto",
                Translations = m.Translations ?? new System.Collections.Generic.Dictionary<string, string>(),
                IsTranslated = m.IsTranslated
            });
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
            [FromServices] IConfiguration configuration,
            [FromServices] Microsoft.Extensions.Hosting.IHostEnvironment environment)
        {
            var enabled = environment.IsDevelopment() || configuration.GetValue<bool>("Messages:EnableRestCreate");
            if (!enabled)
            {
                return NotFound(); // Pretend endpoint absent in production
            }
            if (dto == null || string.IsNullOrWhiteSpace(dto.Room) || string.IsNullOrWhiteSpace(dto.Content))
                return BadRequest();

            var room = await _rooms.GetByNameAsync(dto.Room);
            if (room == null)
                return NotFound();

            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            if (!RoomAccessPolicy.CanAccessRoom(user, room))
            {
                return Forbid();
            }

            var senderDispatchCenterId = RoomAccessPolicy.ResolveDispatchCenterIdForRoom(user, room);

            // Sanitize (strip tags) similar to hub path.
            var sanitized = Regex.Replace(dto.Content, @"<.*?>", string.Empty);
            var message = new Message
            {
                Content = sanitized,
                FromUser = user,
                FromDispatchCenterId = senderDispatchCenterId,
                ToRoom = room,
                Timestamp = DateTime.UtcNow
            };
            try
            {
                message = await _messages.CreateAsync(message);
                await _escalations.ScheduleAutomaticAsync(message);
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
                FromDispatchCenterId = message.FromDispatchCenterId,
                Room = room.Name,
                Timestamp = message.Timestamp,
                CorrelationId = dto.CorrelationId,
                ReadBy = message.ReadBy != null ? message.ReadBy.ToArray() : Array.Empty<string>(),
                ReadByDispatchCenterIds = message.ReadByDispatchCenterIds != null ? message.ReadByDispatchCenterIds.ToArray() : Array.Empty<string>(),
                EscalationStatus = message.EscalationStatus.ToString(),
                OpenEscalationId = message.OpenEscalationId,
                TranslationStatus = message.TranslationStatus.ToString(),
                SourceLanguage = Chat.Web.Utilities.LanguageCode.NormalizeToLanguageCode(message.FromUser?.PreferredLanguage, allowAuto: true) ?? "auto",
                Translations = message.Translations ?? new System.Collections.Generic.Dictionary<string, string>(),
                IsTranslated = message.IsTranslated
            };

            // Fire-and-forget hub broadcast (do not block API latency on network fan-out)
            _ = _hubContext.Clients.Group(room.Name).SendAsync("newMessage", vm);
            metrics?.IncMessagesSent();
            return Created($"/api/Messages/{vm.Id}", vm);
        }

        /// <summary>
        /// Mark a message as read for the current user. Broadcasts update via hub.
        /// </summary>
        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            if (user == null)
            {
                return Forbid();
            }

            var message = await _messages.GetByIdAsync(id);
            var room = await _rooms.GetByNameAsync(message?.ToRoom?.Name);
            if (!RoomAccessPolicy.CanAccessRoom(user, room))
            {
                return NotFound();
            }

            var readDispatchCenterId = RoomAccessPolicy.ResolveDispatchCenterIdForRoom(user, room);
            user.DispatchCenterId = readDispatchCenterId;

            var updated = await _messages.MarkReadAsync(id, user.UserName, readDispatchCenterId);
            if (updated == null) return NotFound();
            await _escalations.ResolveIfAcknowledgedAsync(updated, user);
            // Fire-and-forget broadcast of readers list to the room
            _ = _hubContext.Clients.Group(updated.ToRoom?.Name ?? string.Empty)
                .SendAsync("messageRead", new
                {
                    id = updated.Id,
                    readers = updated.ReadBy?.ToArray() ?? Array.Empty<string>(),
                    readByDispatchCenterIds = updated.ReadByDispatchCenterIds?.ToArray() ?? Array.Empty<string>()
                });
            return NoContent();
        }

        /// <summary>
        /// Manually retry translation for a failed message. Re-queues with high priority.
        /// Requires translation feature to be enabled and message to be in Failed state.
        /// </summary>
        [HttpPost("{id}/retry-translation")]
        public async Task<IActionResult> RetryTranslation(
            int id,
            [FromServices] ITranslationJobQueue translationQueue,
            [FromServices] IOptions<Options.TranslationOptions> translationOptions)
        {
            using var activity = Observability.Tracing.ActivitySource.StartActivity("api.messages.retry-translation");
            activity?.SetTag("message.id", id);
            var translationSettings = translationOptions.Value;
            
            // Check if translation is enabled
            if (!translationSettings.Enabled)
            {
                return BadRequest(new { error = "Translation feature is not enabled" });
            }
            
            var message = await _messages.GetByIdAsync(id);
            if (message == null)
                return NotFound(new { error = "Message not found" });
            
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            var room = await _rooms.GetByNameAsync(message.ToRoom?.Name);
            if (!RoomAccessPolicy.CanAccessRoom(user, room))
            {
                return Forbid();
            }
            
            if (message.TranslationStatus is TranslationStatus.Pending or TranslationStatus.InProgress)
                return BadRequest(new { error = "Translation is already in progress" });

            var senderProfile = await _users.GetByUserNameAsync(message.FromUser?.UserName);
            var sourceLanguage = Chat.Web.Utilities.LanguageCode.NormalizeToLanguageCode(senderProfile?.PreferredLanguage) ?? "auto";

            var targets = Chat.Web.Utilities.LanguageCode.BuildTargetLanguages(room?.Languages, sourceLanguage);
            
            // Create new job with high priority
            var job = new MessageTranslationJob
            {
                MessageId = message.Id,
                RoomName = message.ToRoom.Name,
                Content = message.Content,
                SourceLanguage = sourceLanguage,
                TargetLanguages = targets,
                DeploymentName = translationSettings.DeploymentName,
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0,
                Priority = 10, // High priority for manual retries
                JobId = $"transjob:{message.Id}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
            
            await translationQueue.RequeueAsync(job, highPriority: true);
            await _messages.UpdateTranslationAsync(
                message.Id,
                new MessageTranslationUpdate(
                    Status: TranslationStatus.Pending,
                    Translations: message.Translations ?? new Dictionary<string, string>(),
                    JobId: job.JobId));
            
            _logger.LogInformation("Manual retry triggered for message {MessageId} by user {User}", id, User.Identity.Name);
            
            // Broadcast status update to room
            _ = _hubContext.Clients.Group(message.ToRoom.Name)
                .SendAsync("translationRetrying", new
                {
                    messageId = id,
                    status = "Pending",
                    timestamp = DateTime.UtcNow
                });

            return Ok(new { success = true, jobId = job.JobId });
        }

        public class CreateEscalationDto
        {
            public int[] MessageIds { get; set; } = Array.Empty<int>();
        }

        [HttpPost("/api/escalations")]
        public async Task<IActionResult> Escalate([FromBody] CreateEscalationDto dto)
        {
            var user = await _users.GetByUserNameAsync(User?.Identity?.Name);
            if (user == null)
            {
                return Forbid();
            }

            var messageIds = dto?.MessageIds ?? Array.Empty<int>();
            if (messageIds.Length == 0)
            {
                return BadRequest(new { error = "At least one message must be selected." });
            }

            var firstMessage = await _messages.GetByIdAsync(messageIds[0]);
            if (firstMessage?.ToRoom?.Name == null)
            {
                return NotFound();
            }

            var room = await _rooms.GetByNameAsync(firstMessage.ToRoom.Name);
            var escalationDispatchCenterId = RoomAccessPolicy.ResolveDispatchCenterIdForRoom(user, room);
            if (string.IsNullOrWhiteSpace(escalationDispatchCenterId))
            {
                return Forbid();
            }

            user.DispatchCenterId = escalationDispatchCenterId;

            var escalation = await _escalations.CreateManualAsync(user, firstMessage.ToRoom.Name, messageIds);
            if (escalation == null)
            {
                return BadRequest(new { error = "Escalation could not be created for the selected messages." });
            }

            return Ok(new
            {
                escalationId = escalation.Id,
                status = escalation.Status.ToString(),
                triggerType = escalation.TriggerType.ToString(),
                messageIds = escalation.MessageIds.ToArray()
            });
        }
    }
}
