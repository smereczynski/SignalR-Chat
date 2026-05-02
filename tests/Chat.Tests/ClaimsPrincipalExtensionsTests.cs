using System.Collections.Generic;
using System.Security.Claims;
using Chat.Web.Utilities;
using Xunit;

namespace Chat.Tests;

public class ClaimsPrincipalExtensionsTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IsAdmin_DependsOnAdminReadWriteRole(bool includeAdminRole, bool expected)
    {
        var principal = BuildPrincipal(includeAdminRole ? [new Claim(ClaimTypes.Role, "Admin.ReadWrite")] : [new Claim(ClaimTypes.Role, "User.Read")]);

        Assert.Equal(expected, principal.IsAdmin());
    }

    [Fact]
    public void IsAdmin_NullPrincipal_ReturnsFalse()
    {
        ClaimsPrincipal? principal = null;

        Assert.False(principal.IsAdmin());
    }

    [Fact]
    public void GetRoles_ReturnsAssignedRolesInOrder()
    {
        var principal = BuildPrincipal(
        [
            new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
            new Claim(ClaimTypes.Role, "User.Read")
        ]);

        Assert.Equal(["Admin.ReadWrite", "User.Read"], principal.GetRoles());
    }

    [Theory]
    [InlineData("tid", "12345678-1234-1234-1234-123456789abc", "12345678-1234-1234-1234-123456789abc")]
    [InlineData("preferred_username", "alice@contoso.com", "alice@contoso.com")]
    public void ClaimHelpers_ReturnConfiguredClaimValues(string claimType, string claimValue, string expected)
    {
        var principal = BuildPrincipal([new Claim(claimType, claimValue)]);

        var actual = claimType == "tid" ? principal.GetTenantId() : principal.GetUpn();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ClaimHelpers_MissingOrNullPrincipal_ReturnNullOrEmpty()
    {
        var principal = BuildPrincipal([]);
        ClaimsPrincipal? nullPrincipal = null;

        Assert.Empty(principal.GetRoles());
        Assert.Null(principal.GetTenantId());
        Assert.Null(principal.GetUpn());
        Assert.Empty(nullPrincipal.GetRoles());
        Assert.Null(nullPrincipal.GetTenantId());
        Assert.Null(nullPrincipal.GetUpn());
    }

    private static ClaimsPrincipal BuildPrincipal(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }
}
