using System.Collections.Generic;

namespace Chat.Web.Models
{
    /// <summary>
    /// Organizational unit used to group users and define communication topology.
    /// </summary>
    public class DispatchCenter
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Country { get; set; }
        public bool IfMain { get; set; }

        /// <summary>
        /// Related dispatch centers selected as corresponding for this center.
        /// </summary>
        public ICollection<string> CorrespondingDispatchCenterIds { get; set; } = new List<string>();

        /// <summary>
        /// Denormalized list of usernames assigned to this dispatch center.
        /// </summary>
        public ICollection<string> Users { get; set; } = new List<string>();
    }
}
