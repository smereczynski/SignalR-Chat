using System.Collections.Generic;

namespace Chat.Web.Models
{
    /// <summary>
    /// Lightweight user profile used for authentication, presence display and message attribution.
    /// Extended with fixed channel membership and basic contact details (email/mobile) to support notifications and seeding.
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

    /// <summary>
    /// Preferred starting room. If user has more than one FixedRoom this selects which to auto-join.
    /// If null/empty and only one FixedRoom exists that one is auto-selected; otherwise first FixedRoom alphabetically.
    /// </summary>
    public string DefaultRoom { get; set; }

        public ICollection<Room> Rooms { get; set; }
        public ICollection<Message> Messages { get; set; }
    }
}
