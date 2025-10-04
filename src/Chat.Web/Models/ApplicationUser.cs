using System.Collections.Generic;

namespace Chat.Web.Models
{
    /// <summary>
    /// Lightweight user profile used for authentication, presence display and message attribution.
    /// </summary>
    public class ApplicationUser
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string Avatar { get; set; }
        public ICollection<Room> Rooms { get; set; }
        public ICollection<Message> Messages { get; set; }
    }
}
