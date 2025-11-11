using Chat.Web.Utilities;
using Xunit;

namespace Chat.Tests
{
    /// <summary>
    /// Tests for LogSanitizer utility to verify protection against log forgery attacks (CWE-117).
    /// </summary>
    public class LogSanitizerTests
    {
        [Fact]
        public void Sanitize_RemovesNewlines()
        {
            // Arrange
            var input = "user\nINFO Fake admin login";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.DoesNotContain("\n", result);
            Assert.DoesNotContain("\r", result);
            Assert.Equal("userINFO Fake admin login", result);
        }

        [Fact]
        public void Sanitize_RemovesCarriageReturns()
        {
            // Arrange
            var input = "user\rINFO Malicious entry";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.DoesNotContain("\r", result);
            Assert.Equal("userINFO Malicious entry", result);
        }

        [Fact]
        public void Sanitize_RemovesTabs()
        {
            // Arrange
            var input = "user\tINFO\tFake entry";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.DoesNotContain("\t", result);
            Assert.Equal("userINFOFake entry", result);
        }

        [Fact]
        public void Sanitize_RemovesMultipleControlCharacters()
        {
            // Arrange
            var input = "user\r\n\tINFO Multiple\ncontrol\rchars";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.DoesNotContain("\r", result);
            Assert.DoesNotContain("\n", result);
            Assert.DoesNotContain("\t", result);
            Assert.Equal("userINFO Multiplecontrolchars", result);
        }

        [Fact]
        public void Sanitize_HandlesNullInput()
        {
            // Arrange
            string? input = null;

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Sanitize_HandlesEmptyString()
        {
            // Arrange
            var input = "";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void Sanitize_PreservesNormalText()
        {
            // Arrange
            var input = "normaluser123";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.Equal("normaluser123", result);
        }

        [Fact]
        public void Sanitize_RemovesNullCharacter()
        {
            // Arrange
            var input = "user\0null";

            // Act
            var result = LogSanitizer.Sanitize(input);

            // Assert
            Assert.Equal("usernull", result);
            // Verify the result doesn't contain the null character by checking length
            Assert.Equal(8, result.Length); // "usernull" = 8 chars, not 9 with \0
        }

        [Fact]
        public void SanitizeWithReplacement_ReplacesControlCharacters()
        {
            // Arrange
            var input = "user\r\nINFO Fake entry";

            // Act
            var result = LogSanitizer.SanitizeWithReplacement(input);

            // Assert
            Assert.Contains("�", result);
            Assert.DoesNotContain("\r", result);
            Assert.DoesNotContain("\n", result);
            Assert.Equal("user��INFO Fake entry", result);
        }

        [Fact]
        public void SanitizeWithReplacement_CustomReplacement()
        {
            // Arrange
            var input = "user\ntest";

            // Act
            var result = LogSanitizer.SanitizeWithReplacement(input, "[REMOVED]");

            // Assert
            Assert.Contains("[REMOVED]", result);
            Assert.Equal("user[REMOVED]test", result);
        }

        [Theory]
        [InlineData("admin\nERROR System compromised")]
        [InlineData("user\r\nWARNING Fake alert")]
        [InlineData("test\tINFO\tSpoofed log")]
        public void Sanitize_PreventsLogForgeryAttacks(string maliciousInput)
        {
            // Act
            var result = LogSanitizer.Sanitize(maliciousInput);

            // Assert - Verify no control characters remain
            Assert.DoesNotContain("\n", result);
            Assert.DoesNotContain("\r", result);
            Assert.DoesNotContain("\t", result);
        }
    }
}
