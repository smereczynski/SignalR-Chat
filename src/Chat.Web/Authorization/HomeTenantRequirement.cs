using System;
using System.Threading.Tasks;
using Chat.Web.Options;
using Microsoft.AspNetCore.Authorization;
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

        public HomeTenantHandler(IOptions<EntraIdOptions> entraIdOptions)
        {
            _entraIdOptions = entraIdOptions?.Value ?? throw new ArgumentNullException(nameof(entraIdOptions));
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            HomeTenantRequirement requirement)
        {
            // Get tenant ID from claims (tid is the standard Azure AD tenant ID claim)
            var tenantIdClaim = context.User.FindFirst("tid");
            
            if (tenantIdClaim == null)
            {
                // No tenant ID claim - fail the requirement
                return Task.CompletedTask;
            }

            // Check if the tenant ID matches the configured home tenant
            if (string.Equals(tenantIdClaim.Value, _entraIdOptions.Authorization.HomeTenantId, StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
            }
            // If tenant IDs don't match, the requirement fails (don't call context.Fail to allow other handlers)

            return Task.CompletedTask;
        }
    }
}
