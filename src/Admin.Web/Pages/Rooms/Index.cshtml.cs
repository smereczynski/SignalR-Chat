using Microsoft.AspNetCore.Mvc.RazorPages;

public class RoomsIndexModel : PageModel
{
    private readonly IRoomsRepository _rooms;
    public RoomsIndexModel(IRoomsRepository rooms) => _rooms = rooms;
    public IEnumerable<string> Rooms { get; set; } = Enumerable.Empty<string>();
    public async Task OnGetAsync()
    {
        Rooms = await _rooms.GetAllNamesAsync();
    }
}
