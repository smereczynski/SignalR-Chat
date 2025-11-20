#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Chat.Web.Utilities
{
    /// <summary>
    /// Extension methods for ClaimsPrincipal to simplify role and claim checks.
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Checks if the user has the Admin.ReadWrite app role.
        /// Only home tenant users with this role can access admin panel.
        /// </summary>
        /// <param name="user">The claims principal representing the authenticated user.</param>
        /// <returns>True if user has Admin.ReadWrite role claim; otherwise false.</returns>
        public static bool IsAdmin(this ClaimsPrincipal? user)
        {
            return user?.IsInRole("Admin.ReadWrite") == true;
        }

        /// <summary>
        /// Gets all roles assigned to the user from role claims.
        /// </summary>
        /// <param name="user">The claims principal representing the authenticated user.</param>
        /// <returns">Enumerable of role values; empty if no roles assigned.</returns>
        public static IEnumerable<string> GetRoles(this ClaimsPrincipal? user)
        {
            return user?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets the user's tenant ID from the tid claim.
        /// </summary>
        /// <param name="user">The claims principal representing the authenticated user.</param>
        /// <returns>Tenant ID (GUID) if present; otherwise null.</returns>
        public static string? GetTenantId(this ClaimsPrincipal? user)
        {
            return user?.FindFirst("tid")?.Value;
        }

        /// <summary>
        /// Gets the user's UPN (User Principal Name) from the preferred_username claim.
        /// </summary>
        /// <param name="user">The claims principal representing the authenticated user.</param>
        /// <returns>UPN if present; otherwise null.</returns>
        public static string? GetUpn(this ClaimsPrincipal? user)
        {
            return user?.FindFirst("preferred_username")?.Value;
        }
    }
}
