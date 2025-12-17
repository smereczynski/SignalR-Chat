using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Chat.Web.ViewModels
{
    /// <summary>
    /// Lightweight representation of a chat room returned to clients and broadcast via hub events.
    /// </summary>
    public class RoomViewModel
    {
        public int Id { get; set; }

        [Required]
    // Minimum length lowered from 5 -> 2 to allow seeded short room names like "ops".
    [StringLength(100, ErrorMessageResourceName = "ValidationRoomNameLength", ErrorMessageResourceType = typeof(Resources.SharedResources), MinimumLength = 2)]
        [RegularExpression(@"^\w+( \w+)*$", ErrorMessageResourceName = "ValidationRoomNamePattern", ErrorMessageResourceType = typeof(Resources.SharedResources))]
        public string Name { get; set; }

        /// <summary>
        /// Language codes enabled for this room (e.g., "en", "pl").
        /// </summary>
        public ICollection<string> Languages { get; set; } = new List<string>();

    // Admin field removed
    }
}
