using Chat.Web.Configuration;
using Xunit;

namespace Chat.Tests;

public class ConfigurationGuardsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("YOUR_COSMOS_CONN")] 
    [InlineData("AccountEndpoint=https://<ACCOUNT>.documents.azure.com:443/;AccountKey=<KEY>;")] 
    [InlineData("changeme")] 
    public void IsPlaceholder_ShouldReturnTrue_ForInvalidOrPlaceholder(string? value)
    {
        Assert.True(ConfigurationGuards.IsPlaceholder(value));
    }

    [Theory]
    [InlineData("AccountEndpoint=https://prod.documents.azure.com:443/;AccountKey=abc123==;")]
    [InlineData("redis:6380,password=abc,ssl=True,abortConnect=False")]
    public void IsPlaceholder_ShouldReturnFalse_ForRealisticValues(string value)
    {
        Assert.False(ConfigurationGuards.IsPlaceholder(value));
    }

    [Fact]
    public void Require_ShouldThrow_OnPlaceholder()
    {
        var ex = Assert.Throws<System.InvalidOperationException>(() => ConfigurationGuards.Require("YOUR_KEY", "Test:Key"));
        Assert.Contains("Test:Key", ex.Message);
    }

    [Fact]
    public void Require_ShouldReturn_Value_WhenValid()
    {
        var input = "real-value";
        var result = ConfigurationGuards.Require(input, "Test:Key");
        Assert.Equal(input, result);
    }
}
