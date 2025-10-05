using System.Collections.Generic;

namespace Chat.Web.Models
{
    /// <summary>
    /// Lightweight user profile used for authentication, presence display and message attribution.
    /// Extended with fixed channel membership and basic contact details (email/mobile) for demo seeding.
    /// </summary>
    public class ApplicationUser
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string Avatar { get; set; }
        /// <summary>
        /// Email address for notification / identity enrichment.
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// Mobile number (E.164 formatting recommended) for potential SMS notifications.
        /// </summary>
        public string MobileNumber { get; set; }
        /// <summary>
        /// Fixed list of room names this user is allowed to join. Enforced server-side.
        /// </summary>
        public ICollection<string> FixedRooms { get; set; } = new List<string>();

        public ICollection<Room> Rooms { get; set; }
        public ICollection<Message> Messages { get; set; }
    }
}
