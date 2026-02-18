using System;
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
public class DispatchCentersCreateModel : PageModel
{
    private readonly IDispatchCentersRepository _dispatchCenters;

    public DispatchCentersCreateModel(IDispatchCentersRepository dispatchCenters)
    {
        _dispatchCenters = dispatchCenters;
    }

    [BindProperty]
    public DispatchCenterInputModel Input { get; set; } = new();

    public List<DispatchCenter> AllDispatchCenters { get; set; } = new();

    public async Task OnGetAsync()
    {
        AllDispatchCenters = (await _dispatchCenters.GetAllAsync()).OrderBy(d => d.Name).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        AllDispatchCenters = (await _dispatchCenters.GetAllAsync()).OrderBy(d => d.Name).ToList();

        if (!ModelState.IsValid) return Page();

        var existingByName = await _dispatchCenters.GetByNameAsync(Input.Name.Trim());
        if (existingByName != null)
        {
            ModelState.AddModelError(nameof(Input.Name), "Dispatch center with this name already exists.");
            return Page();
        }

        var normalizedCountry = Input.Country.Trim();
        if (Input.IfMain)
        {
            var mainForCountryExists = AllDispatchCenters.Any(d =>
                d.IfMain &&
                string.Equals(d.Country?.Trim(), normalizedCountry, StringComparison.OrdinalIgnoreCase));

            if (mainForCountryExists)
            {
                ModelState.AddModelError(nameof(Input.Country), "Main dispatch center for this country already exists.");
                return Page();
            }
        }

        var normalizedCorresponding = NormalizeDistinct(Input.CorrespondingDispatchCenterIds);
        foreach (var correspondingId in normalizedCorresponding)
        {
            var existing = await _dispatchCenters.GetByIdAsync(correspondingId);
            if (existing == null)
            {
                ModelState.AddModelError(nameof(Input.CorrespondingDispatchCenterIds), $"Invalid corresponding dispatch center id: {correspondingId}");
                return Page();
            }
        }

        var dispatchCenter = new DispatchCenter
        {
            Id = Guid.NewGuid().ToString(),
            Name = Input.Name.Trim(),
            Country = normalizedCountry,
            IfMain = Input.IfMain,
            CorrespondingDispatchCenterIds = normalizedCorresponding,
            Users = new List<string>()
        };

        await _dispatchCenters.UpsertAsync(dispatchCenter);
        return RedirectToPage("Index");
    }

    private static List<string> NormalizeDistinct(IEnumerable<string> values)
    {
        return (values ?? Enumerable.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
