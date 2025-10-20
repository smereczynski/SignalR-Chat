using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chat.Web.Models
{
    /// <summary>
    /// Represents a chat message posted to a room (FromUser -> Room) with a server-side timestamp.
    /// </summary>
    public class Message
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public ApplicationUser FromUser { get; set; }
        public int ToRoomId { get; set; }
        public Room ToRoom { get; set; }
        // Users who have read this message (usernames)
        public ICollection<string> ReadBy { get; set; } = new List<string>();
    }
}
