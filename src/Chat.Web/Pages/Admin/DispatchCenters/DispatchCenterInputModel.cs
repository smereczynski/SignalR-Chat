using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Chat.Web.Pages.Admin.DispatchCenters;

public class DispatchCenterInputModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Country { get; set; } = string.Empty;

    public bool IfMain { get; set; }

    public List<string> CorrespondingDispatchCenterIds { get; set; } = new();
}
