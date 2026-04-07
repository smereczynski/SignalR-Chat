using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Hubs;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Services
{
    public class EscalationService
    {
        private readonly IEscalationsRepository _escalations;
        private readonly IMessagesRepository _messages;
        private readonly IRoomsRepository _rooms;
        private readonly IUsersRepository _users;
        private readonly IDispatchCentersRepository _dispatchCenters;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly INotificationSender _notifications;
        private readonly ILogger<EscalationService> _logger;

        public EscalationService(
            IEscalationsRepository escalations,
            IMessagesRepository messages,
            IRoomsRepository rooms,
            IUsersRepository users,
            IDispatchCentersRepository dispatchCenters,
            IHubContext<ChatHub> hubContext,
            INotificationSender notifications,
            ILogger<EscalationService> logger)
        {
            _escalations = escalations;
            _messages = messages;
            _rooms = rooms;
            _users = users;
            _dispatchCenters = dispatchCenters;
            _hubContext = hubContext;
            _notifications = notifications;
            _logger = logger;
        }

        public async Task<Escalation> ScheduleAutomaticAsync(Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var room = message.ToRoom ?? await _rooms.GetByIdAsync(message.ToRoomId).ConfigureAwait(false);
            if (room == null || !room.IsActive || !DispatchCenterPairing.IncludesDispatchCenter(room, message.FromDispatchCenterId))
            {
                return null;
            }

            var targetDispatchCenterId = DispatchCenterPairing.GetCounterpartDispatchCenterId(room, message.FromDispatchCenterId);
            var targetDispatchCenter = await _dispatchCenters.GetByIdAsync(targetDispatchCenterId).ConfigureAwait(false);
            var targetOfficerUserNames = targetDispatchCenter?.OfficerUserNames?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targetOfficerUserNames == null || targetOfficerUserNames.Count == 0)
            {
                return null;
            }

            var escalation = new Escalation
            {
                Id = Guid.NewGuid().ToString(),
                RoomName = room.Name,
                PairKey = room.PairKey,
                SourceDispatchCenterId = message.FromDispatchCenterId,
                TargetDispatchCenterId = targetDispatchCenterId,
                TargetOfficerUserNames = targetOfficerUserNames,
                TriggerType = EscalationTriggerType.Automatic,
                Status = Models.EscalationStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                DueAt = message.Timestamp.AddSeconds(300),
                CreatedByUserName = message.FromUser?.UserName,
                MessageIds = new List<int> { message.Id },
                MessageSnapshots = new List<EscalationMessageSnapshot>
                {
                    new EscalationMessageSnapshot
                    {
                        MessageId = message.Id,
                        Content = message.Content,
                        Timestamp = message.Timestamp,
                        FromUserName = message.FromUser?.UserName,
                        FromDispatchCenterId = message.FromDispatchCenterId
                    }
                }
            };

            await _escalations.CreateAsync(escalation).ConfigureAwait(false);
            var updatedMessage = await _messages.UpdateEscalationAsync(message.Id, MessageEscalationStatus.Scheduled, escalation.Id).ConfigureAwait(false);
            message.EscalationStatus = updatedMessage?.EscalationStatus ?? MessageEscalationStatus.Scheduled;
            message.OpenEscalationId = updatedMessage?.OpenEscalationId ?? escalation.Id;
            return escalation;
        }

        public async Task<Escalation> CreateManualAsync(ApplicationUser user, string roomName, IEnumerable<int> messageIds)
        {
            var room = await _rooms.GetByNameAsync(roomName).ConfigureAwait(false);
            if (user == null || room == null || !room.IsActive || !DispatchCenterPairing.IncludesDispatchCenter(room, user.DispatchCenterId))
            {
                return null;
            }

            var ids = (messageIds ?? Array.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0)
            {
                return null;
            }

            var messages = new List<Message>();
            foreach (var id in ids)
            {
                var message = await _messages.GetByIdAsync(id).ConfigureAwait(false);
                if (message == null || !string.Equals(message.ToRoom?.Name, room.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!string.Equals(message.FromDispatchCenterId, user.DispatchCenterId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var targetDispatchCenterId = DispatchCenterPairing.GetCounterpartDispatchCenterId(room, user.DispatchCenterId);
                if ((message.ReadByDispatchCenterIds ?? Array.Empty<string>()).Contains(targetDispatchCenterId, StringComparer.OrdinalIgnoreCase))
                {
                    return null;
                }

                var openEscalation = await _escalations.GetOpenByMessageIdAsync(message.Id).ConfigureAwait(false);
                if (openEscalation != null)
                {
                    return null;
                }

                messages.Add(message);
            }

            var counterpartId = DispatchCenterPairing.GetCounterpartDispatchCenterId(room, user.DispatchCenterId);
            var counterpart = await _dispatchCenters.GetByIdAsync(counterpartId).ConfigureAwait(false);
            var counterpartOfficerUserNames = counterpart?.OfficerUserNames?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (counterpartOfficerUserNames == null || counterpartOfficerUserNames.Count == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var escalation = new Escalation
            {
                Id = Guid.NewGuid().ToString(),
                RoomName = room.Name,
                PairKey = room.PairKey,
                SourceDispatchCenterId = user.DispatchCenterId,
                TargetDispatchCenterId = counterpartId,
                TargetOfficerUserNames = counterpartOfficerUserNames,
                TriggerType = EscalationTriggerType.Manual,
                Status = Models.EscalationStatus.Escalated,
                CreatedAt = now,
                DueAt = now,
                EscalatedAt = now,
                CreatedByUserName = user.UserName,
                MessageIds = messages.Select(x => x.Id).ToList(),
                MessageSnapshots = messages.Select(x => new EscalationMessageSnapshot
                {
                    MessageId = x.Id,
                    Content = x.Content,
                    Timestamp = x.Timestamp,
                    FromUserName = x.FromUser?.UserName,
                    FromDispatchCenterId = x.FromDispatchCenterId
                }).ToList()
            };

            await _escalations.CreateAsync(escalation).ConfigureAwait(false);
            foreach (var message in messages)
            {
                await _messages.UpdateEscalationAsync(message.Id, MessageEscalationStatus.Escalated, escalation.Id).ConfigureAwait(false);
            }

            await PublishEscalationChangedAsync(escalation, room, messages.First()).ConfigureAwait(false);
            return escalation;
        }

        public async Task ResolveIfAcknowledgedAsync(Message message, ApplicationUser reader)
        {
            if (message == null || reader == null || string.IsNullOrWhiteSpace(reader.DispatchCenterId))
            {
                return;
            }

            if (string.Equals(message.FromDispatchCenterId, reader.DispatchCenterId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var escalation = !string.IsNullOrWhiteSpace(message.OpenEscalationId)
                ? await _escalations.GetByIdAsync(message.OpenEscalationId, message.ToRoom?.Name).ConfigureAwait(false)
                : await _escalations.GetOpenByMessageIdAsync(message.Id).ConfigureAwait(false);
            if (escalation == null || escalation.Status == Models.EscalationStatus.Resolved)
            {
                return;
            }

            escalation.Status = Models.EscalationStatus.Resolved;
            escalation.ResolvedAt = DateTime.UtcNow;
            escalation.CancelledAt = null;
            await _escalations.UpsertAsync(escalation).ConfigureAwait(false);

            foreach (var messageId in escalation.MessageIds)
            {
                await _messages.UpdateEscalationAsync(messageId, MessageEscalationStatus.Resolved, null).ConfigureAwait(false);
            }

            var room = await _rooms.GetByNameAsync(escalation.RoomName).ConfigureAwait(false);
            if (room != null)
            {
                await _hubContext.Clients.Group(room.Name).SendAsync("escalationChanged", new
                {
                    escalationId = escalation.Id,
                    status = escalation.Status.ToString(),
                    messageIds = escalation.MessageIds.ToArray(),
                    triggerType = escalation.TriggerType.ToString(),
                    targetOfficerUserNames = escalation.TargetOfficerUserNames?.ToArray() ?? Array.Empty<string>(),
                    timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
        }

        public async Task ProcessDueScheduledAsync(CancellationToken cancellationToken)
        {
            var due = await _escalations.GetDueScheduledAsync(DateTime.UtcNow, 100).ConfigureAwait(false);
            foreach (var escalation in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var room = await _rooms.GetByNameAsync(escalation.RoomName).ConfigureAwait(false);
                if (room == null)
                {
                    continue;
                }

                var messages = new List<Message>();
                foreach (var messageId in escalation.MessageIds)
                {
                    var message = await _messages.GetByIdAsync(messageId).ConfigureAwait(false);
                    if (message != null)
                    {
                        messages.Add(message);
                    }
                }

                var acknowledged = messages.Any(x =>
                    (x.ReadByDispatchCenterIds ?? Array.Empty<string>())
                    .Contains(escalation.TargetDispatchCenterId, StringComparer.OrdinalIgnoreCase));

                if (acknowledged)
                {
                    escalation.Status = Models.EscalationStatus.Resolved;
                    escalation.ResolvedAt = DateTime.UtcNow;
                    await _escalations.UpsertAsync(escalation).ConfigureAwait(false);

                    foreach (var messageId in escalation.MessageIds)
                    {
                        await _messages.UpdateEscalationAsync(messageId, MessageEscalationStatus.Resolved, null).ConfigureAwait(false);
                    }

                    continue;
                }

                escalation.Status = Models.EscalationStatus.Escalated;
                escalation.EscalatedAt = DateTime.UtcNow;
                await _escalations.UpsertAsync(escalation).ConfigureAwait(false);

                foreach (var messageId in escalation.MessageIds)
                {
                    await _messages.UpdateEscalationAsync(messageId, MessageEscalationStatus.Escalated, escalation.Id).ConfigureAwait(false);
                }

                var referenceMessage = messages.FirstOrDefault();
                if (referenceMessage != null)
                {
                    await PublishEscalationChangedAsync(escalation, room, referenceMessage).ConfigureAwait(false);
                }
            }
        }

        private async Task PublishEscalationChangedAsync(Escalation escalation, Room room, Message referenceMessage)
        {
            await _hubContext.Clients.Group(room.Name).SendAsync("escalationChanged", new
            {
                escalationId = escalation.Id,
                status = escalation.Status.ToString(),
                messageIds = escalation.MessageIds.ToArray(),
                triggerType = escalation.TriggerType.ToString(),
                targetOfficerUserNames = escalation.TargetOfficerUserNames?.ToArray() ?? Array.Empty<string>(),
                timestamp = DateTime.UtcNow
            }).ConfigureAwait(false);

            var officerUserNames = escalation.TargetOfficerUserNames?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
            foreach (var officerUserName in officerUserNames)
            {
                var officer = await _users.GetByUserNameAsync(officerUserName).ConfigureAwait(false);
                if (officer != null)
                {
                    await _notifications.NotifyAsync(officer, room.DisplayName ?? room.Name, referenceMessage).ConfigureAwait(false);
                }
            }

            _logger.LogInformation(
                "Escalation {EscalationId} transitioned to {Status} for room {Room}",
                escalation.Id,
                escalation.Status,
                room.Name);
        }
    }
}
