using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Chat.Web.Pages.Admin.DispatchCenters;

[Authorize(Policy = "RequireAdminRole")]
public class DispatchCentersEditModel : PageModel
{
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly IUsersRepository _users;
    private readonly IRoomsRepository _rooms;
    private readonly Services.DispatchCenterTopologyService _topology;

    public DispatchCentersEditModel(IDispatchCentersRepository dispatchCenters, IUsersRepository users, IRoomsRepository rooms, Services.DispatchCenterTopologyService topology)
    {
        _dispatchCenters = dispatchCenters;
        _users = users;
        _rooms = rooms;
        _topology = topology;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public DispatchCenterInputModel Input { get; set; } = new();

    public List<DispatchCenter> AllDispatchCenters { get; set; } = new();
    public List<SelectListItem> OfficerUsers { get; set; } = new();

    // Impact section (read-only)
    public List<ApplicationUser> AssignedUsers { get; set; } = new();
    public List<DispatchCenter> ReciprocalPairs { get; set; } = new();
    public List<Room> DerivedPairRooms { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return RedirectToPage("Index");

        var current = await _dispatchCenters.GetByIdAsync(Id);
        if (current == null) return RedirectToPage("Index");

        Input = new DispatchCenterInputModel
        {
            Name = current.Name,
            Country = current.Country,
            IfMain = current.IfMain,
            OfficerUserNames = current.OfficerUserNames?.ToList() ?? new List<string>(),
            CorrespondingDispatchCenterIds = current.CorrespondingDispatchCenterIds?.ToList() ?? new List<string>()
        };

        await LoadFormDataAsync().ConfigureAwait(false);
        await LoadImpactDataAsync().ConfigureAwait(false);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return RedirectToPage("Index");

        var current = await _dispatchCenters.GetByIdAsync(Id);
        if (current == null) return RedirectToPage("Index");

        await LoadFormDataAsync().ConfigureAwait(false);

        if (!ModelState.IsValid)
        {
            await LoadImpactDataAsync().ConfigureAwait(false);
            return Page();
        }

        var existingByName = await _dispatchCenters.GetByNameAsync(Input.Name.Trim());
        if (existingByName != null && !string.Equals(existingByName.Id, Id, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Input.Name), "Dispatch center with this name already exists.");
            await LoadImpactDataAsync().ConfigureAwait(false);
            return Page();
        }

        var normalizedCorresponding = NormalizeDistinct(Input.CorrespondingDispatchCenterIds);
        if (normalizedCorresponding.Any(x => string.Equals(x, Id, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(Input.CorrespondingDispatchCenterIds), "Self-reference is not allowed.");
            await LoadImpactDataAsync().ConfigureAwait(false);
            return Page();
        }

        foreach (var correspondingId in normalizedCorresponding)
        {
            var existing = await _dispatchCenters.GetByIdAsync(correspondingId);
            if (existing == null)
            {
                ModelState.AddModelError(nameof(Input.CorrespondingDispatchCenterIds), $"Invalid corresponding dispatch center id: {correspondingId}");
                await LoadImpactDataAsync().ConfigureAwait(false);
                return Page();
            }
        }

        var officerUserNames = NormalizeDistinct(Input.OfficerUserNames);
        if (officerUserNames.Count == 0)
        {
            ModelState.AddModelError(nameof(Input.OfficerUserNames), "Select at least one escalation officer.");
            await LoadImpactDataAsync().ConfigureAwait(false);
            return Page();
        }

        foreach (var officerUserName in officerUserNames)
        {
            var officer = await _users.GetByUserNameAsync(officerUserName);
            if (officer == null)
            {
                ModelState.AddModelError(nameof(Input.OfficerUserNames), $"Officer user was not found: {officerUserName}");
                await LoadImpactDataAsync().ConfigureAwait(false);
                return Page();
            }
        }

        current.Name = Input.Name.Trim();
        current.Country = Input.Country.Trim();
        current.IfMain = Input.IfMain;
        current.OfficerUserNames = officerUserNames;
        current.CorrespondingDispatchCenterIds = normalizedCorresponding;

        await _topology.SaveDispatchCenterAsync(current, current.CorrespondingDispatchCenterIds);
        TempData["SuccessMessage"] = "DispatchCenterSaved";
        return RedirectToPage("Index");
    }

    private async Task LoadFormDataAsync()
    {
        AllDispatchCenters = (await _dispatchCenters.GetAllAsync())
            .Where(d => !string.Equals(d.Id, Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();
        OfficerUsers = (await _users.GetAllAsync())
            .Where(x => x.Enabled)
            .OrderBy(x => x.FullName ?? x.UserName)
            .Select(x => new SelectListItem(x.FullName ?? x.UserName, x.UserName))
            .ToList();
        if (PageContext?.ViewData != null)
        {
            PageContext.ViewData["OfficerUsers"] = OfficerUsers;
        }
    }

    private async Task LoadImpactDataAsync()
    {
        var usersTask = _users.GetByDispatchCenterIdAsync(Id);
        var allDcsTask = _dispatchCenters.GetAllAsync();
        var allRoomsTask = _rooms.GetAllAsync();

        await Task.WhenAll(usersTask, allDcsTask, allRoomsTask).ConfigureAwait(false);

        AssignedUsers = (await usersTask).OrderBy(u => u.UserName).ToList();

        ReciprocalPairs = (await allDcsTask)
            .Where(d => !string.Equals(d.Id, Id, StringComparison.OrdinalIgnoreCase) &&
                        (d.CorrespondingDispatchCenterIds?.Contains(Id, StringComparer.OrdinalIgnoreCase) ?? false))
            .OrderBy(d => d.Name)
            .ToList();

        DerivedPairRooms = (await allRoomsTask)
            .Where(r => string.Equals(r.DispatchCenterAId, Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.DispatchCenterBId, Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name)
            .ToList();
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
