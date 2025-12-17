using Chat.Web.Utilities;
using Xunit;

namespace Chat.Tests;

public class PreferredLanguageMergerTests
{
    [Theory]
    [InlineData("pl", null, "pl")]
    [InlineData("pl", "en", "pl")]
    [InlineData("  pl  ", "en", "  pl  ")]
    [InlineData(null, "en", "en")]
    [InlineData("", "en", "en")]
    [InlineData("   ", "en", "en")]
    [InlineData(null, null, null)]
    public void Merge_IncomingVsExisting_Works(string? incoming, string? existing, string? expected)
    {
        var actual = PreferredLanguageMerger.Merge(incoming, existing);
        Assert.Equal(expected, actual);
    }
}
