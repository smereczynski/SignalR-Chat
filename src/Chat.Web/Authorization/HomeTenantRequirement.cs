using System;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Web.Authorization
{
    /// <summary>
    /// Authorization requirement that ensures the user belongs to the configured home tenant.
    /// External tenant users are denied access even if they have the required app role.
    /// </summary>
    public class HomeTenantRequirement : IAuthorizationRequirement
    {
    }

    /// <summary>
    /// Authorization handler that validates the user's tenant ID matches the configured home tenant.
    /// </summary>
    public class HomeTenantHandler : AuthorizationHandler<HomeTenantRequirement>
    {
        private readonly EntraIdOptions _entraIdOptions;
        private readonly ILogger<HomeTenantHandler> _logger;

        public HomeTenantHandler(IOptions<EntraIdOptions> entraIdOptions, ILogger<HomeTenantHandler> logger)
        {
            _entraIdOptions = entraIdOptions?.Value ?? throw new ArgumentNullException(nameof(entraIdOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            HomeTenantRequirement requirement)
        {
            // Get tenant ID from claims - try both short form and full URI
            // Azure AD may use either "tid" or the full claim URI depending on configuration
            var tenantIdClaim = context.User.FindFirst("tid") 
                ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid");
            
            var configuredHomeTenantId = _entraIdOptions.Authorization.HomeTenantId;
            
            _logger.LogInformation(
                "HomeTenant validation - User: {UserName}, TID claim: {UserTenantId}, Configured HomeTenantId: {ConfiguredHomeTenantId}",
                context.User.Identity?.Name ?? "Unknown",
                tenantIdClaim?.Value ?? "MISSING",
                string.IsNullOrEmpty(configuredHomeTenantId) ? "NOT_CONFIGURED" : configuredHomeTenantId
            );
            
            if (tenantIdClaim == null)
            {
                // Log only claim types (not values) to avoid exposing sensitive information
                var claimTypes = string.Join(", ", context.User.Claims.Select(c => c.Type));
                _logger.LogWarning("HomeTenant validation FAILED: No tenant ID claim found in user token. Available claim types: {ClaimTypes}", claimTypes);
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(configuredHomeTenantId))
            {
                _logger.LogWarning("HomeTenant validation FAILED: HomeTenantId is not configured in appsettings");
                return Task.CompletedTask;
            }

            // Check if the tenant ID matches the configured home tenant
            if (string.Equals(tenantIdClaim.Value, configuredHomeTenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("HomeTenant validation SUCCEEDED: User tenant {UserTenantId} matches configured home tenant", tenantIdClaim.Value);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning(
                    "HomeTenant validation FAILED: User tenant {UserTenantId} does not match configured home tenant {ConfiguredHomeTenantId}",
                    tenantIdClaim.Value,
                    configuredHomeTenantId
                );
            }

            return Task.CompletedTask;
        }
    }
}
