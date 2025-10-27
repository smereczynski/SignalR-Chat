using System.ComponentModel.DataAnnotations;

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

    // Admin field removed
    }
}
