using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Controllers
{
    [Authorize(Policy = "RequireAdminRole")]
    [Route("api/[controller]")]
    [ApiController]
    public class DispatchCentersController : ControllerBase
    {
        private readonly IDispatchCentersRepository _dispatchCenters;
        private readonly IUsersRepository _users;
        private readonly Services.DispatchCenterTopologyService _topology;
        private readonly ILogger<DispatchCentersController> _logger;

        public DispatchCentersController(
            IDispatchCentersRepository dispatchCenters,
            IUsersRepository users,
            Services.DispatchCenterTopologyService topology,
            ILogger<DispatchCentersController> logger)
        {
            _dispatchCenters = dispatchCenters;
            _users = users;
            _topology = topology;
            _logger = logger;
        }

        public class UpsertDispatchCenterDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Country { get; set; }
            public bool IfMain { get; set; }
            public List<string> OfficerUserNames { get; set; } = new();
            public List<string> CorrespondingDispatchCenterIds { get; set; } = new();
        }

        public class ManageUsersDto
        {
            public List<string> UserNames { get; set; } = new();
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var all = (await _dispatchCenters.GetAllAsync().ConfigureAwait(false))
                .OrderBy(d => d.Name)
                .ToList();
            return Ok(all);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");

            var dispatchCenter = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
            if (dispatchCenter == null) return NotFound();

            return Ok(dispatchCenter);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertDispatchCenterDto dto)
        {
            if (dto == null) return BadRequest("request body is required");

            var id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString() : dto.Id.Trim();
            var name = dto.Name?.Trim();
            var country = dto.Country?.Trim();

            if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required");
            if (string.IsNullOrWhiteSpace(country)) return BadRequest("country is required");

            var existingByName = await _dispatchCenters.GetByNameAsync(name).ConfigureAwait(false);
            if (existingByName != null)
            {
                return Conflict("dispatch center with the same name already exists");
            }

            if (dto.IfMain)
            {
                var allDispatchCenters = await _dispatchCenters.GetAllAsync().ConfigureAwait(false);
                var mainForCountryExists = allDispatchCenters.Any(d =>
                    d.IfMain &&
                    string.Equals(d.Country?.Trim(), country, StringComparison.OrdinalIgnoreCase));

                if (mainForCountryExists)
                {
                    return Conflict("main dispatch center for this country already exists");
                }
            }

            var validationError = await ValidateCorrespondingIdsAsync(id, dto.CorrespondingDispatchCenterIds).ConfigureAwait(false);
            if (validationError != null) return BadRequest(validationError);

            var officerUserNames = NormalizeDistinct(dto.OfficerUserNames);
            if (officerUserNames.Count == 0)
            {
                return BadRequest("at least one officerUserName is required");
            }

            var missingOfficer = await FindMissingUserAsync(officerUserNames).ConfigureAwait(false);
            if (missingOfficer != null)
            {
                return BadRequest($"user not found: {missingOfficer}");
            }

            var entity = new DispatchCenter
            {
                Id = id,
                Name = name,
                Country = country,
                IfMain = dto.IfMain,
                CorrespondingDispatchCenterIds = NormalizeDistinct(dto.CorrespondingDispatchCenterIds),
                Users = new List<string>(),
                OfficerUserNames = officerUserNames
            };

            await _topology.SaveDispatchCenterAsync(entity, entity.CorrespondingDispatchCenterIds).ConfigureAwait(false);

            _logger.LogInformation(
                "Dispatch center created: id={DispatchCenterId}, name={Name}",
                LogSanitizer.Sanitize(entity.Id),
                LogSanitizer.Sanitize(entity.Name));

            return Created($"/api/DispatchCenters/{entity.Id}", entity);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpsertDispatchCenterDto dto)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");
            if (dto == null) return BadRequest("request body is required");

            var current = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
            if (current == null) return NotFound();

            var name = dto.Name?.Trim();
            var country = dto.Country?.Trim();

            if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required");
            if (string.IsNullOrWhiteSpace(country)) return BadRequest("country is required");

            var existingByName = await _dispatchCenters.GetByNameAsync(name).ConfigureAwait(false);
            if (existingByName != null && !string.Equals(existingByName.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict("dispatch center with the same name already exists");
            }

            var validationError = await ValidateCorrespondingIdsAsync(id, dto.CorrespondingDispatchCenterIds).ConfigureAwait(false);
            if (validationError != null) return BadRequest(validationError);

            var officerUserNames = NormalizeDistinct(dto.OfficerUserNames);
            if (officerUserNames.Count == 0)
            {
                return BadRequest("at least one officerUserName is required");
            }

            var missingOfficer = await FindMissingUserAsync(officerUserNames).ConfigureAwait(false);
            if (missingOfficer != null)
            {
                return BadRequest($"user not found: {missingOfficer}");
            }

            current.Name = name;
            current.Country = country;
            current.IfMain = dto.IfMain;
            current.OfficerUserNames = officerUserNames;
            current.CorrespondingDispatchCenterIds = NormalizeDistinct(dto.CorrespondingDispatchCenterIds);

            await _topology.SaveDispatchCenterAsync(current, current.CorrespondingDispatchCenterIds).ConfigureAwait(false);

            _logger.LogInformation(
                "Dispatch center updated: id={DispatchCenterId}, name={Name}",
                LogSanitizer.Sanitize(current.Id),
                LogSanitizer.Sanitize(current.Name));

            return Ok(current);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");

            var current = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
            if (current == null) return NotFound();

            await _topology.DeleteDispatchCenterAsync(id).ConfigureAwait(false);

            _logger.LogInformation("Dispatch center deleted: id={DispatchCenterId}", LogSanitizer.Sanitize(id));

            return NoContent();
        }

        [HttpPost("{id}/users/assign")]
        public async Task<IActionResult> AssignUsers(string id, [FromBody] ManageUsersDto dto)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");
            if (dto == null) return BadRequest("request body is required");

            var dispatchCenter = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
            if (dispatchCenter == null) return NotFound();

            var targetUsers = NormalizeDistinct(dto.UserNames);
            foreach (var userName in targetUsers)
            {
                var user = await _users.GetByUserNameAsync(userName).ConfigureAwait(false);
                if (user == null)
                {
                    return BadRequest($"user not found: {userName}");
                }
            }

            await _topology.AssignUsersAsync(id, targetUsers).ConfigureAwait(false);

            return Ok(await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false));
        }

        [HttpPost("{id}/users/unassign")]
        public async Task<IActionResult> UnassignUsers(string id, [FromBody] ManageUsersDto dto)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");
            if (dto == null) return BadRequest("request body is required");

            var dispatchCenter = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
            if (dispatchCenter == null) return NotFound();

            var targetUsers = NormalizeDistinct(dto.UserNames);
            await _topology.RemoveUsersFromDispatchCenterAsync(id, targetUsers).ConfigureAwait(false);

            return Ok(await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false));
        }

        [HttpPut("{id}/corresponding")]
        public async Task<IActionResult> ReplaceCorresponding(string id, [FromBody] List<string> correspondingIds)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");

            var dispatchCenter = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
            if (dispatchCenter == null) return NotFound();

            var validationError = await ValidateCorrespondingIdsAsync(id, correspondingIds).ConfigureAwait(false);
            if (validationError != null) return BadRequest(validationError);

            dispatchCenter.CorrespondingDispatchCenterIds = NormalizeDistinct(correspondingIds);
            await _topology.SaveDispatchCenterAsync(dispatchCenter, dispatchCenter.CorrespondingDispatchCenterIds).ConfigureAwait(false);

            return Ok(dispatchCenter);
        }

        private async Task<string> ValidateCorrespondingIdsAsync(string dispatchCenterId, IEnumerable<string> correspondingIds)
        {
            var normalized = NormalizeDistinct(correspondingIds);

            if (normalized.Any(x => string.Equals(x, dispatchCenterId, StringComparison.OrdinalIgnoreCase)))
            {
                return "self-reference is not allowed in correspondingDispatchCenterIds";
            }

            foreach (var id in normalized)
            {
                var existing = await _dispatchCenters.GetByIdAsync(id).ConfigureAwait(false);
                if (existing == null)
                {
                    return $"invalid corresponding dispatch center id: {id}";
                }
            }

            return null;
        }

        private async Task<string> FindMissingUserAsync(IEnumerable<string> userNames)
        {
            foreach (var userName in NormalizeDistinct(userNames))
            {
                var user = await _users.GetByUserNameAsync(userName).ConfigureAwait(false);
                if (user == null)
                {
                    return userName;
                }
            }

            return null;
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
}
