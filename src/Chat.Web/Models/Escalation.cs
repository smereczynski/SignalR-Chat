using System;
using System.Collections.Generic;

namespace Chat.Web.Models
{
    public enum EscalationTriggerType
    {
        Automatic = 0,
        Manual = 1
    }

    public enum EscalationStatus
    {
        Scheduled = 0,
        Escalated = 1,
        Resolved = 2,
        Cancelled = 3
    }

    public class EscalationMessageSnapshot
    {
        public int MessageId { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string FromUserName { get; set; }
        public string FromDispatchCenterId { get; set; }
    }

    public class Escalation
    {
        public string Id { get; set; }
        public string RoomName { get; set; }
        public string PairKey { get; set; }
        public string SourceDispatchCenterId { get; set; }
        public string TargetDispatchCenterId { get; set; }
        public ICollection<string> TargetOfficerUserNames { get; set; } = new List<string>();
        public EscalationTriggerType TriggerType { get; set; }
        public EscalationStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime DueAt { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CreatedByUserName { get; set; }
        public ICollection<int> MessageIds { get; set; } = new List<int>();
        public ICollection<EscalationMessageSnapshot> MessageSnapshots { get; set; } = new List<EscalationMessageSnapshot>();
    }
}
