using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Chat.Web.Utilities;
using Xunit;

namespace Chat.Tests
{
    /// <summary>
    /// Unit tests for ClaimsPrincipalExtensions helper methods.
    /// Tests role checking, claim extraction, and null handling.
    /// </summary>
    public class ClaimsPrincipalExtensionsTests
    {
        [Fact]
        public void IsAdmin_WithAdminRole_ReturnsTrue()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "alice"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.IsAdmin();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsAdmin_WithoutAdminRole_ReturnsFalse()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "bob"),
                new Claim(ClaimTypes.Role, "User.Read")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.IsAdmin();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsAdmin_WithNullPrincipal_ReturnsFalse()
        {
            // Arrange
            ClaimsPrincipal? principal = null;

            // Act
            var result = principal.IsAdmin();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsAdmin_WithNoRoles_ReturnsFalse()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "charlie")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.IsAdmin();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetRoles_WithMultipleRoles_ReturnsAllRoles()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "dave"),
                new Claim(ClaimTypes.Role, "Admin.ReadWrite"),
                new Claim(ClaimTypes.Role, "User.Read")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var roles = principal.GetRoles().ToList();

            // Assert
            Assert.Equal(2, roles.Count);
            Assert.Contains("Admin.ReadWrite", roles);
            Assert.Contains("User.Read", roles);
        }

        [Fact]
        public void GetRoles_WithNoRoles_ReturnsEmpty()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "eve")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var roles = principal.GetRoles().ToList();

            // Assert
            Assert.Empty(roles);
        }

        [Fact]
        public void GetRoles_WithNullPrincipal_ReturnsEmpty()
        {
            // Arrange
            ClaimsPrincipal? principal = null;

            // Act
            var roles = principal.GetRoles().ToList();

            // Assert
            Assert.Empty(roles);
        }

        [Fact]
        public void GetTenantId_WithTidClaim_ReturnsTenantId()
        {
            // Arrange
            var tenantId = "12345678-1234-1234-1234-123456789abc";
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "alice"),
                new Claim("tid", tenantId)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.GetTenantId();

            // Assert
            Assert.Equal(tenantId, result);
        }

        [Fact]
        public void GetTenantId_WithoutTidClaim_ReturnsNull()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "bob")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.GetTenantId();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetTenantId_WithNullPrincipal_ReturnsNull()
        {
            // Arrange
            ClaimsPrincipal? principal = null;

            // Act
            var result = principal.GetTenantId();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetUpn_WithPreferredUsernameClaim_ReturnsUpn()
        {
            // Arrange
            var upn = "alice@contoso.com";
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "alice"),
                new Claim("preferred_username", upn)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.GetUpn();

            // Assert
            Assert.Equal(upn, result);
        }

        [Fact]
        public void GetUpn_WithoutPreferredUsernameClaim_ReturnsNull()
        {
            // Arrange
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "charlie")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = principal.GetUpn();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetUpn_WithNullPrincipal_ReturnsNull()
        {
            // Arrange
            ClaimsPrincipal? principal = null;

            // Act
            var result = principal.GetUpn();

            // Assert
            Assert.Null(result);
        }
    }
}
