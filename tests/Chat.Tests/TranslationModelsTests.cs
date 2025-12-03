using System;
using System.Collections.Generic;
using System.Linq;
using Chat.Web.Models;
using Chat.Web.Services;
using Xunit;

namespace Chat.Tests;

/// <summary>
/// Unit tests for translation domain models: MessageTranslationJob, TranslateRequest, TranslateResponse.
/// Tests serialization, validation, and business logic.
/// </summary>
public class TranslationModelsTests
{
    [Fact]
    public void MessageTranslationJob_Serialization_ShouldPreserveAllProperties()
    {
        // Arrange
        var job = new MessageTranslationJob
        {
            JobId = "job-123",
            MessageId = 456,
            RoomName = "general",
            Content = "Hello world",
            SourceLanguage = "en",
            TargetLanguages = new List<string> { "en", "pl", "de" },
            DeploymentName = "gpt-4o-mini",
            CreatedAt = new DateTime(2025, 12, 3, 10, 30, 0, DateTimeKind.Utc),
            Priority = 5,
            RetryCount = 2
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(job);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<MessageTranslationJob>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(job.JobId, deserialized.JobId);
        Assert.Equal(job.MessageId, deserialized.MessageId);
        Assert.Equal(job.RoomName, deserialized.RoomName);
        Assert.Equal(job.Content, deserialized.Content);
        Assert.Equal(job.SourceLanguage, deserialized.SourceLanguage);
        Assert.Equal(job.TargetLanguages.Count, deserialized.TargetLanguages.Count);
        Assert.Equal(job.DeploymentName, deserialized.DeploymentName);
        Assert.Equal(job.Priority, deserialized.Priority);
        Assert.Equal(job.RetryCount, deserialized.RetryCount);
    }

    [Fact]
    public void TranslateRequest_WithAutoLanguage_ShouldSetSourceToAuto()
    {
        // Arrange & Act
        var request = new TranslateRequest
        {
            Text = "Bonjour le monde",
            SourceLanguage = "auto",
            Targets = new List<TranslationTarget>
            {
                new() { Language = "en", DeploymentName = "gpt-4o-mini" }
            }
        };

        // Assert
        Assert.Equal("auto", request.SourceLanguage);
    }

    [Fact]
    public void TranslateResponse_WithDetectedLanguage_ShouldHaveScoreAndLanguage()
    {
        // Arrange & Act
        var response = new TranslateResponse
        {
            Translations = new List<Translation>
            {
                new() { Language = "en", Text = "Hello world" }
            },
            DetectedLanguage = "fr",
            DetectedLanguageScore = 0.95,
            FromCache = false
        };

        // Assert
        Assert.Equal("fr", response.DetectedLanguage);
        Assert.Equal(0.95, response.DetectedLanguageScore);
        Assert.False(response.FromCache);
        Assert.Single(response.Translations);
    }

    [Fact]
    public void Translation_WithLanguageAndText_ShouldBeValid()
    {
        // Arrange & Act
        var result = new Translation
        {
            Language = "pl",
            Text = "Witaj świecie"
        };

        // Assert
        Assert.Equal("pl", result.Language);
        Assert.Equal("Witaj świecie", result.Text);
    }

    [Fact]
    public void TranslationStatus_EnumValues_ShouldMatchExpectedStates()
    {
        // Assert
        Assert.Equal(0, (int)TranslationStatus.None);
        Assert.Equal(1, (int)TranslationStatus.Pending);
        Assert.Equal(2, (int)TranslationStatus.InProgress);
        Assert.Equal(3, (int)TranslationStatus.Completed);
        Assert.Equal(4, (int)TranslationStatus.Failed);
    }

    [Fact]
    public void TranslationStatus_ToString_ShouldReturnEnumName()
    {
        // Arrange
        var status = TranslationStatus.Completed;

        // Act
        var str = status.ToString();

        // Assert
        Assert.Equal("Completed", str);
    }

    [Fact]
    public void Message_WithTranslations_ShouldHaveIsTranslatedTrue()
    {
        // Arrange
        var message = new Message
        {
            Id = 123,
            Content = "Hello",
            TranslationStatus = TranslationStatus.Completed,
            Translations = new Dictionary<string, string>
            {
                { "en", "Hello" },
                { "pl", "Cześć" }
            }
        };

        // Act
        var isTranslated = message.IsTranslated;

        // Assert
        Assert.True(isTranslated);
    }

    [Fact]
    public void Message_WithoutTranslations_ShouldHaveIsTranslatedFalse()
    {
        // Arrange
        var message = new Message
        {
            Id = 123,
            Content = "Hello",
            TranslationStatus = TranslationStatus.None,
            Translations = null
        };

        // Act
        var isTranslated = message.IsTranslated;

        // Assert
        Assert.False(isTranslated);
    }

    [Fact]
    public void Message_WithEmptyTranslations_ShouldHaveIsTranslatedFalse()
    {
        // Arrange
        var message = new Message
        {
            Id = 123,
            Content = "Hello",
            TranslationStatus = TranslationStatus.Pending,
            Translations = new Dictionary<string, string>()
        };

        // Act
        var isTranslated = message.IsTranslated;

        // Assert
        Assert.False(isTranslated);
    }

    [Fact]
    public void TranslationTarget_WithLanguageAndDeployment_ShouldBeValid()
    {
        // Arrange & Act
        var target = new TranslationTarget
        {
            Language = "de",
            DeploymentName = "gpt-4o-mini"
        };

        // Assert
        Assert.Equal("de", target.Language);
        Assert.Equal("gpt-4o-mini", target.DeploymentName);
    }

    [Fact]
    public void MessageTranslationJob_WithIncrementedRetryCount_ShouldTrackRetries()
    {
        // Arrange
        var job = new MessageTranslationJob
        {
            JobId = "job-123",
            MessageId = 456,
            RoomName = "general",
            Content = "Test",
            SourceLanguage = "en",
            TargetLanguages = new List<string> { "en", "pl" },
            RetryCount = 0
        };

        // Act
        job.RetryCount++;
        job.RetryCount++;

        // Assert
        Assert.Equal(2, job.RetryCount);
    }

    [Fact]
    public void TranslateRequest_WithMultipleTargets_ShouldSupportMultipleLanguages()
    {
        // Arrange & Act
        var request = new TranslateRequest
        {
            Text = "Hello world",
            SourceLanguage = "en",
            Targets = new List<TranslationTarget>
            {
                new() { Language = "en", DeploymentName = "gpt-4o-mini" },
                new() { Language = "pl", DeploymentName = "gpt-4o-mini" },
                new() { Language = "de", DeploymentName = "gpt-4o-mini" },
                new() { Language = "fr", DeploymentName = "gpt-4o-mini" },
                new() { Language = "es", DeploymentName = "gpt-4o-mini" }
            },
            Tone = "casual"
        };

        // Assert
        Assert.Equal(5, request.Targets.Count);
        Assert.Equal("casual", request.Tone);
        Assert.All(request.Targets, t => Assert.Equal("gpt-4o-mini", t.DeploymentName));
    }

    [Fact]
    public void TranslateResponse_WithCachedResult_ShouldHaveFromCacheTrue()
    {
        // Arrange & Act
        var response = new TranslateResponse
        {
            Translations = new List<Translation>
            {
                new() { Language = "en", Text = "Hello" }
            },
            FromCache = true,
            DetectedLanguage = "en",
            DetectedLanguageScore = 1.0
        };

        // Assert
        Assert.True(response.FromCache);
    }
}
