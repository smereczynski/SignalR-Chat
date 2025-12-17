using System.Collections.Generic;
using Chat.Web.Utilities;
using Xunit;

namespace Chat.Tests;

public class LanguageCodeTests
{
    public static IEnumerable<object?[]> NormalizeToLanguageCode_CommonInputs => new List<object?[]>
    {
        new object?[] { null, null },
        new object?[] { string.Empty, null },
        new object?[] { "  ", null },
        new object?[] { "pl", "pl" },
        new object?[] { "PL", "pl" },
        new object?[] { "pl-PL", "pl" },
        new object?[] { "pl_PL", "pl" },
        new object?[] { "en", "en" },
        new object?[] { "auto", null },
        new object?[] { " auto ", null }
    };

    [Theory]
    [MemberData(nameof(NormalizeToLanguageCode_CommonInputs))]
    public void NormalizeToLanguageCode_CommonInputs_Works(string? input, string? expected)
    {
        var actual = LanguageCode.NormalizeToLanguageCode(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData(" Auto ", "auto")]
    public void NormalizeToLanguageCode_AllowAuto_ReturnsAuto(string input, string expected)
    {
        var actual = LanguageCode.NormalizeToLanguageCode(input, allowAuto: true);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildTargetLanguages_AddsEnglish_Dedupes_RemovesAuto_AndSkipsSource()
    {
        var roomLanguages = new List<string> { "pl-PL", "de", "AUTO", "", "pl" };

        var targets = LanguageCode.BuildTargetLanguages(roomLanguages, sourceLanguage: "pl");

        Assert.Contains("en", targets);
        Assert.Contains("de", targets);
        Assert.DoesNotContain("auto", targets);
        Assert.DoesNotContain("pl", targets);
    }
}
