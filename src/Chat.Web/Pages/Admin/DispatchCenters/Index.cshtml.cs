using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.DispatchCenters;

[Authorize(Policy = "RequireAdminRole")]
public class DispatchCentersIndexModel : PageModel
{
    private readonly IDispatchCentersRepository _dispatchCenters;

    public DispatchCentersIndexModel(IDispatchCentersRepository dispatchCenters)
    {
        _dispatchCenters = dispatchCenters;
    }

    public IEnumerable<DispatchCenter> DispatchCenters { get; set; } = Enumerable.Empty<DispatchCenter>();

    public async Task OnGetAsync()
    {
        DispatchCenters = (await _dispatchCenters.GetAllAsync()).OrderBy(d => d.Name);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToPage();

        await _dispatchCenters.DeleteAsync(id);
        return RedirectToPage();
    }
}
