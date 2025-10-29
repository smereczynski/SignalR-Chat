using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Tests;

public class LocalizationTests
{
    private readonly IStringLocalizer<Chat.Web.Resources.SharedResources> _localizer;
    
    public LocalizationTests()
    {
        // Setup DI container with localization services
        var services = new ServiceCollection();
        services.AddLogging(); // Required by ResourceManagerStringLocalizerFactory
        services.AddLocalization();
        
        var serviceProvider = services.BuildServiceProvider();
        _localizer = serviceProvider.GetRequiredService<IStringLocalizer<Chat.Web.Resources.SharedResources>>();
    }
    
    [Fact]
    public void Localizer_ShouldReturnEnglishValues_WhenDefaultCultureIsUsed()
    {
        // Arrange & Act
        var appTitle = _localizer["AppTitle"];
        var appDescription = _localizer["AppDescription"];
        var signInToContinue = _localizer["SignInToContinue"];
        
        // Assert
        Assert.False(appTitle.ResourceNotFound, "AppTitle resource should be found");
        Assert.False(appDescription.ResourceNotFound, "AppDescription resource should be found");
        Assert.False(signInToContinue.ResourceNotFound, "SignInToContinue resource should be found");
        
        Assert.Equal("SignalR Chat", appTitle.Value);
        Assert.Equal("Real-time chat application.", appDescription.Value);
        Assert.Equal("Sign in to continue", signInToContinue.Value);
    }
    
    [Fact]
    public void Localizer_ShouldReturnValues_ForAllCommonStrings()
    {
        // Arrange - Test common strings used in the application
        var testKeys = new[]
        {
            "Loading",
            "Error",
            "Retry",
            "Search",
            "ChatRooms",
            "User",
            "SendCode",
            "Code",
            "Verify",
            "Today",
            "Yesterday"
        };
        
        // Act & Assert
        foreach (var key in testKeys)
        {
            var localizedString = _localizer[key];
            Assert.False(localizedString.ResourceNotFound, $"{key} resource should be found");
            Assert.NotEmpty(localizedString.Value);
            // Note: We don't assert that value != key because some English words
            // like "Error", "User", "Code" are the same in the key and value
        }
    }
    
    [Fact]
    public void Localizer_ShouldReturnKey_WhenResourceNotFound()
    {
        // Arrange
        var nonExistentKey = "ThisKeyDoesNotExist_12345";
        
        // Act
        var localizedString = _localizer[nonExistentKey];
        
        // Assert
        Assert.True(localizedString.ResourceNotFound, "Non-existent resource should not be found");
        Assert.Equal(nonExistentKey, localizedString.Value); // Should return the key as fallback
    }
    
    [Fact]
    public void Localizer_ShouldSupportParameterizedStrings()
    {
        // Arrange
        var key = "WhosHere";
        var count = 5;
        
        // Act
        var localizedString = _localizer[key, count];
        
        // Assert
        Assert.False(localizedString.ResourceNotFound, "WhosHere resource should be found");
        Assert.Contains("5", localizedString.Value); // Should contain the parameter
    }
    
    [Theory]
    [InlineData("Loading")]
    [InlineData("Error")]
    [InlineData("User")]
    [InlineData("SendCode")]
    [InlineData("Verify")]
    public void Localizer_ShouldNotReturnEmptyString_ForKnownKeys(string key)
    {
        // Act
        var localizedString = _localizer[key];
        
        // Assert
        Assert.False(localizedString.ResourceNotFound);
        Assert.NotEmpty(localizedString.Value);
        Assert.NotEqual(string.Empty, localizedString.Value);
    }
    
    [Fact]
    public void Localizer_ShouldReturnDifferentValues_ForDifferentKeys()
    {
        // Arrange & Act
        var loading = _localizer["Loading"].Value;
        var error = _localizer["Error"].Value;
        var retry = _localizer["Retry"].Value;
        
        // Assert - Each key should have a unique value
        Assert.NotEqual(loading, error);
        Assert.NotEqual(loading, retry);
        Assert.NotEqual(error, retry);
    }
}
