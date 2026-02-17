using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin.DispatchCenters;

[Authorize(Policy = "RequireAdminRole")]
public class DispatchCentersEditModel : PageModel
{
    private readonly IDispatchCentersRepository _dispatchCenters;

    public DispatchCentersEditModel(IDispatchCentersRepository dispatchCenters)
    {
        _dispatchCenters = dispatchCenters;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public class InputModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Country { get; set; } = string.Empty;

        public bool IfMain { get; set; }

        public List<string> CorrespondingDispatchCenterIds { get; set; } = new();
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<DispatchCenter> AllDispatchCenters { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return RedirectToPage("Index");

        var current = await _dispatchCenters.GetByIdAsync(Id);
        if (current == null) return RedirectToPage("Index");

        Input = new InputModel
        {
            Name = current.Name,
            Country = current.Country,
            IfMain = current.IfMain,
            CorrespondingDispatchCenterIds = current.CorrespondingDispatchCenterIds?.ToList() ?? new List<string>()
        };

        AllDispatchCenters = (await _dispatchCenters.GetAllAsync())
            .Where(d => !string.Equals(d.Id, Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return RedirectToPage("Index");

        var current = await _dispatchCenters.GetByIdAsync(Id);
        if (current == null) return RedirectToPage("Index");

        AllDispatchCenters = (await _dispatchCenters.GetAllAsync())
            .Where(d => !string.Equals(d.Id, Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();

        if (!ModelState.IsValid) return Page();

        var existingByName = await _dispatchCenters.GetByNameAsync(Input.Name.Trim());
        if (existingByName != null && !string.Equals(existingByName.Id, Id, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Input.Name), "Dispatch center with this name already exists.");
            return Page();
        }

        var normalizedCorresponding = NormalizeDistinct(Input.CorrespondingDispatchCenterIds);
        if (normalizedCorresponding.Any(x => string.Equals(x, Id, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(Input.CorrespondingDispatchCenterIds), "Self-reference is not allowed.");
            return Page();
        }

        foreach (var correspondingId in normalizedCorresponding)
        {
            var existing = await _dispatchCenters.GetByIdAsync(correspondingId);
            if (existing == null)
            {
                ModelState.AddModelError(nameof(Input.CorrespondingDispatchCenterIds), $"Invalid corresponding dispatch center id: {correspondingId}");
                return Page();
            }
        }

        current.Name = Input.Name.Trim();
        current.Country = Input.Country.Trim();
        current.IfMain = Input.IfMain;
        current.CorrespondingDispatchCenterIds = normalizedCorresponding;

        await _dispatchCenters.UpsertAsync(current);
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
