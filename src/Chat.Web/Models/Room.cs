using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chat.Web.Models
{
    public class Room
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ICollection<Message> Messages { get; set; }
        // Denormalized list of user names assigned to this room (for quick lookup / admin views)
        public ICollection<string> Users { get; set; } = new List<string>();

        /// <summary>
        /// Denormalized list of language codes active in this room (e.g., "en", "pl").
        /// Used as translation targets for newly sent messages. Old messages are not backfilled.
        /// </summary>
        public ICollection<string> Languages { get; set; } = new List<string>();
    }
}
