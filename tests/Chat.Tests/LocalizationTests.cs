using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Tests;

public class LocalizationTests
{
    private IStringLocalizer<Chat.Web.Resources.SharedResources> CreateLocalizer(string cultureName)
    {
        // Setup DI container with localization services
        var services = new ServiceCollection();
        services.AddLogging(); // Required by ResourceManagerStringLocalizerFactory
        services.AddLocalization();
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Set the culture for this test
        var culture = new CultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        
        return serviceProvider.GetRequiredService<IStringLocalizer<Chat.Web.Resources.SharedResources>>();
    }
    
    [Fact]
    public void Localizer_ShouldReturnEnglishValues_WhenDefaultCultureIsUsed()
    {
        // Arrange
        var localizer = CreateLocalizer("en");
        
        // Act
        var appTitle = localizer["AppTitle"];
        var appDescription = localizer["AppDescription"];
        var signInToContinue = localizer["SignInToContinue"];
        
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
        // Arrange
        var localizer = CreateLocalizer("en");
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
            var localizedString = localizer[key];
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
        var localizer = CreateLocalizer("en");
        var nonExistentKey = "ThisKeyDoesNotExist_12345";
        
        // Act
        var localizedString = localizer[nonExistentKey];
        
        // Assert
        Assert.True(localizedString.ResourceNotFound, "Non-existent resource should not be found");
        Assert.Equal(nonExistentKey, localizedString.Value); // Should return the key as fallback
    }
    
    [Fact]
    public void Localizer_ShouldSupportParameterizedStrings()
    {
        // Arrange
        var localizer = CreateLocalizer("en");
        var key = "WhosHere";
        var count = 5;
        
        // Act
        var localizedString = localizer[key, count];
        
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
        // Arrange
        var localizer = CreateLocalizer("en");
        
        // Act
        var localizedString = localizer[key];
        
        // Assert
        Assert.False(localizedString.ResourceNotFound);
        Assert.NotEmpty(localizedString.Value);
        Assert.NotEqual(string.Empty, localizedString.Value);
    }
    
    [Fact]
    public void Localizer_ShouldReturnDifferentValues_ForDifferentKeys()
    {
        // Arrange
        var localizer = CreateLocalizer("en");
        
        // Act
        var loading = localizer["Loading"].Value;
        var error = localizer["Error"].Value;
        var retry = localizer["Retry"].Value;
        
        // Assert - Each key should have a unique value
        Assert.NotEqual(loading, error);
        Assert.NotEqual(loading, retry);
        Assert.NotEqual(error, retry);
    }
    
    // ============================================================================
    // Multi-Locale Tests - All 9 Markets
    // ============================================================================
    
    [Theory]
    [InlineData("en", "SignalR Chat", "Real-time chat application.", "Loading…")]
    [InlineData("pl-PL", "SignalR Czat", "Aplikacja czatu w czasie rzeczywistym.", "Ładowanie…")]
    [InlineData("de-DE", "SignalR Chat", "Echtzeit-Chat-Anwendung.", "Wird geladen…")]
    [InlineData("cs-CZ", "SignalR Chat", "Chatovací aplikace v reálném čase.", "Načítání…")]
    [InlineData("sk-SK", "SignalR Chat", "Chatovacia aplikácia v reálnom čase.", "Načítava sa…")]
    [InlineData("uk-UA", "SignalR Чат", "Чат-додаток у реальному часі.", "Завантаження…")]
    [InlineData("be-BY", "SignalR Чат", "Чат-прыкладанне ў рэжыме рэальнага часу.", "Загрузка…")]
    [InlineData("lt-LT", "SignalR pokalbiai", "Pokalbių programa realiuoju laiku.", "Įkeliama…")]
    [InlineData("ru-RU", "SignalR чат", "Чат-приложение в режиме реального времени.", "Загрузка…")]
    public void Localizer_ShouldReturnCorrectTranslations_ForAllLocales(
        string cultureName,
        string expectedAppTitle,
        string expectedAppDescription,
        string expectedLoading)
    {
        // Arrange
        var localizer = CreateLocalizer(cultureName);
        
        // Act
        var appTitle = localizer["AppTitle"];
        var appDescription = localizer["AppDescription"];
        var loading = localizer["Loading"];
        
        // Assert
        Assert.False(appTitle.ResourceNotFound, $"AppTitle should be found for {cultureName}");
        Assert.False(appDescription.ResourceNotFound, $"AppDescription should be found for {cultureName}");
        Assert.False(loading.ResourceNotFound, $"Loading should be found for {cultureName}");
        
        Assert.Equal(expectedAppTitle, appTitle.Value);
        Assert.Equal(expectedAppDescription, appDescription.Value);
        Assert.Equal(expectedLoading, loading.Value);
    }
    
    [Theory]
    [InlineData("en", "Error", "Chat Rooms", "Sign in to continue")]
    [InlineData("pl-PL", "Błąd", "Pokoje czatu", "Zaloguj się, aby kontynuować")]
    [InlineData("de-DE", "Fehler", "Chaträume", "Anmelden, um fortzufahren")]
    [InlineData("cs-CZ", "Chyba", "Chatovací místnosti", "Přihlaste se a pokračujte")]
    [InlineData("sk-SK", "Chyba", "Chatovacie miestnosti", "Prihláste sa a pokračujte")]
    [InlineData("uk-UA", "Помилка", "Чат-кімнати", "Увійдіть, щоб продовжити")]
    [InlineData("be-BY", "Памылка", "Чат-пакоі", "Увайдзіце, каб працягнуць")]
    [InlineData("lt-LT", "Klaida", "Pokalbių kambariai", "Prisijunkite, kad tęstumėte")]
    [InlineData("ru-RU", "Ошибка", "Комнаты чата", "Войдите, чтобы продолжить")]
    public void Localizer_ShouldReturnCorrectUIStrings_ForAllLocales(
        string cultureName,
        string expectedError,
        string expectedChatRooms,
        string expectedSignInToContinue)
    {
        // Arrange
        var localizer = CreateLocalizer(cultureName);
        
        // Act
        var error = localizer["Error"];
        var chatRooms = localizer["ChatRooms"];
        var signInToContinue = localizer["SignInToContinue"];
        
        // Assert
        Assert.False(error.ResourceNotFound, $"Error should be found for {cultureName}");
        Assert.False(chatRooms.ResourceNotFound, $"ChatRooms should be found for {cultureName}");
        Assert.False(signInToContinue.ResourceNotFound, $"SignInToContinue should be found for {cultureName}");
        
        Assert.Equal(expectedError, error.Value);
        Assert.Equal(expectedChatRooms, chatRooms.Value);
        Assert.Equal(expectedSignInToContinue, signInToContinue.Value);
    }
    
    [Theory]
    [InlineData("en", "Send code", "Verify", "6-digit code")]
    [InlineData("pl-PL", "Wyślij kod", "Zweryfikuj", "6-cyfrowy kod")]
    [InlineData("de-DE", "Code senden", "Bestätigen", "6-stelliger Code")]
    [InlineData("cs-CZ", "Odeslat kód", "Ověřit", "6místný kód")]
    [InlineData("sk-SK", "Odoslať kód", "Overiť", "6-miestny kód")]
    [InlineData("uk-UA", "Надіслати код", "Підтвердити", "6-значний код")]
    [InlineData("be-BY", "Даслаць код", "Пацвердзіць", "6-лічбавы код")]
    [InlineData("lt-LT", "Siųsti kodą", "Patvirtinti", "6 skaitmenų kodas")]
    [InlineData("ru-RU", "Отправить код", "Подтвердить", "6-значный код")]
    public void Localizer_ShouldReturnCorrectAuthStrings_ForAllLocales(
        string cultureName,
        string expectedSendCode,
        string expectedVerify,
        string expectedSixDigitCode)
    {
        // Arrange
        var localizer = CreateLocalizer(cultureName);
        
        // Act
        var sendCode = localizer["SendCode"];
        var verify = localizer["Verify"];
        var sixDigitCode = localizer["SixDigitCode"];
        
        // Assert
        Assert.False(sendCode.ResourceNotFound, $"SendCode should be found for {cultureName}");
        Assert.False(verify.ResourceNotFound, $"Verify should be found for {cultureName}");
        Assert.False(sixDigitCode.ResourceNotFound, $"SixDigitCode should be found for {cultureName}");
        
        Assert.Equal(expectedSendCode, sendCode.Value);
        Assert.Equal(expectedVerify, verify.Value);
        Assert.Equal(expectedSixDigitCode, sixDigitCode.Value);
    }
    
    [Theory]
    [InlineData("en")]
    [InlineData("pl-PL")]
    [InlineData("de-DE")]
    [InlineData("cs-CZ")]
    [InlineData("sk-SK")]
    [InlineData("uk-UA")]
    [InlineData("be-BY")]
    [InlineData("lt-LT")]
    [InlineData("ru-RU")]
    public void Localizer_ShouldHaveAllRequiredKeys_ForAllLocales(string cultureName)
    {
        // Arrange
        var localizer = CreateLocalizer(cultureName);
        var requiredKeys = new[]
        {
            "AppTitle",
            "AppDescription",
            "Loading",
            "Error",
            "Retry",
            "Search",
            "GoToChat",
            "SignInToContinue",
            "SignIn",
            "ChatRooms",
            "SelectRoomToJoin",
            "NoMessages",
            "MessageInputPlaceholder",
            "MessagesWaitingToSend",
            "WhosHere",
            "Reconnecting",
            "Disconnected",
            "MessageFailed",
            "MessagePending",
            "User",
            "SendCode",
            "Code",
            "SixDigitCode",
            "CodeExpires",
            "Resend",
            "Verify",
            "SendingCode",
            "SentToEmailAndMobile",
            "FailedToSend",
            "Today",
            "Yesterday",
            "AM",
            "PM",
            "ErrorOccurred",
            "RequestId",
            "DevelopmentMode",
            "DevelopmentModeWarning",
            "FailedToLoadUsers",
            "PleaseWaitSeconds",
            "FailedToSendCode",
            "ErrorSendingCode",
            "InvalidVerificationCode",
            "VerificationFailed",
            "LoginSuccessful",
            "SessionExpired",
            "SendingTooQuickly",
            "ErrorJoinRoomNameRequired",
            "ErrorNotAuthorizedRoom",
            "ErrorUserProfileNotFound",
            "ErrorJoinRoom",
            "ErrorNotInRoom",
            "ErrorRateLimitExceeded",
            "EmailSubjectVerificationCode",
            "EmailSubjectNewMessage",
            "EmailBodyVerificationCode",
            "SmsBodyVerificationCode",
            "ValidationRoomNameLength",
            "ValidationRoomNamePattern"
        };
        
        // Act & Assert
        foreach (var key in requiredKeys)
        {
            var localizedString = localizer[key];
            Assert.False(
                localizedString.ResourceNotFound,
                $"Key '{key}' should be found for culture '{cultureName}'");
            Assert.NotEmpty(localizedString.Value);
        }
    }
    
    [Theory]
    [InlineData("en", 5, "Who's Here (5)")]
    [InlineData("pl-PL", 3, "Kto tu jest (3)")]
    [InlineData("de-DE", 10, "Anwesend (10)")]
    [InlineData("cs-CZ", 7, "Kdo je tady (7)")]
    [InlineData("sk-SK", 4, "Kto je tu (4)")]
    [InlineData("uk-UA", 6, "Хто тут (6)")]
    [InlineData("be-BY", 8, "Хто тут (8)")]
    [InlineData("lt-LT", 2, "Kas čia (2)")]
    [InlineData("ru-RU", 9, "Кто здесь (9)")]
    public void Localizer_ShouldSupportParameterizedStrings_ForAllLocales(
        string cultureName,
        int count,
        string expectedOutput)
    {
        // Arrange
        var localizer = CreateLocalizer(cultureName);
        
        // Act
        var localizedString = localizer["WhosHere", count];
        
        // Assert
        Assert.False(localizedString.ResourceNotFound, $"WhosHere should be found for {cultureName}");
        Assert.Equal(expectedOutput, localizedString.Value);
    }
}
