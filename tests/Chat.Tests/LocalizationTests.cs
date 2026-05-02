using System;
using System.Globalization;
using Chat.Web.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Chat.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("pl-PL")]
    [InlineData("de-DE")]
    [InlineData("cs-CZ")]
    [InlineData("sk-SK")]
    [InlineData("uk-UA")]
    [InlineData("lt-LT")]
    [InlineData("ru-RU")]
    public void SharedResources_ExposeCoreKeys_ForSupportedCultures(string cultureName)
    {
        var keys = new[] { "AppTitle", "Loading", "Error", "SignInToContinue", "WhosHere" };

        using var scope = new CultureScope(cultureName);
        using var provider = BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();

        foreach (var key in keys)
        {
            var value = localizer[key];
            Assert.False(value.ResourceNotFound, $"{key} should exist for {cultureName}");
            Assert.False(string.IsNullOrWhiteSpace(value.Value), $"{key} should have a value for {cultureName}");
        }
    }

    [Theory]
    [InlineData("en", 5)]
    [InlineData("pl-PL", 3)]
    [InlineData("de-DE", 10)]
    [InlineData("cs-CZ", 7)]
    [InlineData("sk-SK", 4)]
    [InlineData("uk-UA", 6)]
    [InlineData("lt-LT", 2)]
    [InlineData("ru-RU", 9)]
    public void SharedResources_FormatsParameterizedStrings_ForSupportedCultures(string cultureName, int count)
    {
        using var scope = new CultureScope(cultureName);
        using var provider = BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();

        var value = localizer["WhosHere", count];

        Assert.False(value.ResourceNotFound);
        Assert.Contains(count.ToString(CultureInfo.CurrentCulture), value.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedResources_MissingKey_FallsBackToKeyName()
    {
        using var scope = new CultureScope("en");
        using var provider = BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();

        var value = localizer["ThisKeyDoesNotExist_12345"];

        Assert.True(value.ResourceNotFound);
        Assert.Equal("ThisKeyDoesNotExist_12345", value.Value);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        return services.BuildServiceProvider();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUiCulture;

        public CultureScope(string cultureName)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUiCulture = CultureInfo.CurrentUICulture;

            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
