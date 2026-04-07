using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;
using Chat.Web.Models;

namespace Chat.Web.Pages.Admin.Users;

[Authorize(Policy = "RequireAdminRole")]
public class UsersCreateModel : PageModel
{
    private readonly IUsersRepository _users;
    private readonly IDispatchCentersRepository _dispatchCenters;
    private readonly Services.DispatchCenterTopologyService _topology;

    public UsersCreateModel(IUsersRepository users, IDispatchCentersRepository dispatchCenters, Services.DispatchCenterTopologyService topology)
    {
        _users = users;
        _dispatchCenters = dispatchCenters;
        _topology = topology;
    }

    public class InputModel
    {
        [Required]
        public string UserName { get; set; } = string.Empty;
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string MobileNumber { get; set; } = string.Empty;
        
        [EmailAddress]
        public string Upn { get; set; } = string.Empty;

        [Required]
        public string DispatchCenterId { get; set; } = string.Empty;
        
        public bool Enabled { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IEnumerable<DispatchCenter> DispatchCenters { get; set; } = new List<DispatchCenter>();

    public async Task OnGet()
    {
        var dispatchCenters = await _dispatchCenters.GetAllAsync();
        DispatchCenters = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(dispatchCenters, x => x.Name));
    }

    public async Task<IActionResult> OnPost()
    {
        var dispatchCenters = await _dispatchCenters.GetAllAsync();
        DispatchCenters = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(dispatchCenters, x => x.Name));
        if (!ModelState.IsValid) return Page();
        var dispatchCenter = await _dispatchCenters.GetByIdAsync(Input.DispatchCenterId);
        if (dispatchCenter == null)
        {
            ModelState.AddModelError(nameof(Input.DispatchCenterId), "Dispatch center is required.");
            return Page();
        }
        var user = new ApplicationUser
        {
            UserName = Input.UserName,
            Email = Input.Email,
            MobileNumber = Input.MobileNumber,
            Upn = !string.IsNullOrWhiteSpace(Input.Upn) ? Input.Upn : null,
            Enabled = Input.Enabled,
            DispatchCenterId = Input.DispatchCenterId
        };
        await _users.UpsertAsync(user);
        await _topology.AssignUserAsync(Input.DispatchCenterId, user.UserName);
        return RedirectToPage("Index");
    }
}
