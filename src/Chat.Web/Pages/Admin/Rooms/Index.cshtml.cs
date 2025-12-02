using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;

namespace Chat.Web.Pages.Admin.Rooms;

[Authorize(Policy = "RequireAdminRole")]
public class RoomsIndexModel : PageModel
{
    private readonly IRoomsRepository _rooms;
    public RoomsIndexModel(IRoomsRepository rooms) => _rooms = rooms;
    public IEnumerable<string> Rooms { get; set; } = Enumerable.Empty<string>();
    public async Task OnGetAsync()
    {
        Rooms = (await _rooms.GetAllAsync()).Select(r => r.Name);
    }
}
