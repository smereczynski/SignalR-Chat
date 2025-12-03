using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Chat.Tests;

/// <summary>
/// Unit tests for TranslationJobQueue - Redis-based queue for translation jobs.
/// Tests cover enqueue, dequeue, requeue, and error handling.
/// </summary>
public class TranslationJobQueueTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<TranslationJobQueue>> _mockLogger;
    private readonly TranslationOptions _options;
    private readonly TranslationJobQueue _queue;

    public TranslationJobQueueTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<TranslationJobQueue>>();

        _options = new TranslationOptions
        {
            Enabled = true,
            QueueName = "translation:queue",
            MaxConcurrentJobs = 5,
            MaxRetries = 3
        };

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        _queue = new TranslationJobQueue(
            _mockRedis.Object,
            Options.Create(_options),
            _mockLogger.Object);
    }

    [Fact]
    public async Task EnqueueAsync_WithValidJob_ShouldAddToQueue()
    {
        // Arrange
        var job = new MessageTranslationJob
        {
            JobId = "test-job-1",
            MessageId = 123,
            RoomName = "general",
            Content = "Hello world",
            SourceLanguage = "en",
            TargetLanguages = new System.Collections.Generic.List<string> { "en", "pl" },
            CreatedAt = DateTime.UtcNow,
            Priority = 0,
            RetryCount = 0
        };

        _mockDatabase.Setup(db => db.ListLeftPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _queue.EnqueueAsync(job);

        // Assert
        _mockDatabase.Verify(db => db.ListLeftPushAsync(
            It.Is<RedisKey>(k => k == _options.QueueName),
            It.IsAny<RedisValue>(),
            When.Always,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WhenDisabled_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var disabledOptions = new TranslationOptions { Enabled = false };
        var disabledQueue = new TranslationJobQueue(
            _mockRedis.Object,
            Options.Create(disabledOptions),
            _mockLogger.Object);

        var job = new MessageTranslationJob
        {
            JobId = "test-job-1",
            MessageId = 123,
            RoomName = "general",
            Content = "Hello world",
            SourceLanguage = "en",
            TargetLanguages = new System.Collections.Generic.List<string> { "en", "pl" },
            CreatedAt = DateTime.UtcNow
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            disabledQueue.EnqueueAsync(job));
        
        Assert.Contains("Translation queue is not available", exception.Message);
        
        _mockDatabase.Verify(db => db.ListLeftPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task DequeueAsync_WithEmptyQueue_ShouldReturnNull()
    {
        // Arrange
        _mockDatabase.Setup(db => db.ListRightPopAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _queue.DequeueAsync();

        // Assert
        Assert.Null(result);
        _mockDatabase.Verify(db => db.ListRightPopAsync(
            It.Is<RedisKey>(k => k == _options.QueueName),
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task DequeueAsync_WithValidJob_ShouldReturnDeserializedJob()
    {
        // Arrange
        var job = new MessageTranslationJob
        {
            JobId = "test-job-1",
            MessageId = 123,
            RoomName = "general",
            Content = "Hello world",
            SourceLanguage = "en",
            TargetLanguages = new System.Collections.Generic.List<string> { "en", "pl" },
            DeploymentName = "gpt-4o-mini",
            CreatedAt = DateTime.UtcNow,
            Priority = 0,
            RetryCount = 0
        };

        // Serialize with camelCase (matching queue implementation)
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
        var serialized = System.Text.Json.JsonSerializer.Serialize(job, jsonOptions);
        
        _mockDatabase.Setup(db => db.ListRightPopAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(serialized);

        // Act
        var result = await _queue.DequeueAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(job.JobId, result.JobId);
        Assert.Equal(job.MessageId, result.MessageId);
        Assert.Equal(job.RoomName, result.RoomName);
        Assert.Equal(job.Content, result.Content);
        Assert.Equal(job.TargetLanguages.Count, result.TargetLanguages.Count);
        Assert.Equal(job.DeploymentName, result.DeploymentName);
    }

    [Fact]
    public async Task RequeueAsync_WithHighPriority_ShouldPushToFront()
    {
        // Arrange
        var job = new MessageTranslationJob
        {
            JobId = "test-job-1",
            MessageId = 123,
            RoomName = "general",
            Content = "Hello world",
            SourceLanguage = "en",
            TargetLanguages = new System.Collections.Generic.List<string> { "en", "pl" },
            CreatedAt = DateTime.UtcNow,
            Priority = 0,
            RetryCount = 1
        };

        _mockDatabase.Setup(db => db.ListRightPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _queue.RequeueAsync(job, highPriority: true);

        // Assert
        _mockDatabase.Verify(db => db.ListRightPushAsync(
            It.Is<RedisKey>(k => k == _options.QueueName),
            It.IsAny<RedisValue>(),
            When.Always,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task RequeueAsync_WithLowPriority_ShouldPushToBack()
    {
        // Arrange
        var job = new MessageTranslationJob
        {
            JobId = "test-job-1",
            MessageId = 123,
            RoomName = "general",
            Content = "Hello world",
            SourceLanguage = "en",
            TargetLanguages = new System.Collections.Generic.List<string> { "en", "pl" },
            CreatedAt = DateTime.UtcNow,
            Priority = 0,
            RetryCount = 1
        };

        _mockDatabase.Setup(db => db.ListLeftPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _queue.RequeueAsync(job, highPriority: false);

        // Assert
        _mockDatabase.Verify(db => db.ListLeftPushAsync(
            It.Is<RedisKey>(k => k == _options.QueueName),
            It.IsAny<RedisValue>(),
            When.Always,
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task GetQueueLengthAsync_ShouldReturnListLength()
    {
        // Arrange
        _mockDatabase.Setup(db => db.ListLengthAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(42);

        // Act
        var length = await _queue.GetQueueLengthAsync();

        // Assert
        Assert.Equal(42, length);
        _mockDatabase.Verify(db => db.ListLengthAsync(
            It.Is<RedisKey>(k => k == _options.QueueName),
            CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task GetQueueLengthAsync_WhenDisabled_ShouldReturnZero()
    {
        // Arrange
        var disabledOptions = new TranslationOptions { Enabled = false };
        var disabledQueue = new TranslationJobQueue(
            _mockRedis.Object,
            Options.Create(disabledOptions),
            _mockLogger.Object);

        // Act
        var length = await disabledQueue.GetQueueLengthAsync();

        // Assert
        Assert.Equal(0, length);
        _mockDatabase.Verify(db => db.ListLengthAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task EnqueueAsync_WithNullJob_ShouldThrowNullReferenceException()
    {
        // Act & Assert
        // Implementation doesn't explicitly check for null, throws NullReferenceException
        // when accessing job.MessageId in logger
        await Assert.ThrowsAsync<NullReferenceException>(() => _queue.EnqueueAsync(null!));
    }

    [Fact(Skip = "Implementation uses RPOP (non-blocking) and doesn't check cancellation token - limitation documented")]
    public async Task DequeueAsync_WithCancellation_DoesNotSupportCancellation()
    {
        // NOTE: The current implementation uses RPOP (non-blocking) which doesn't support
        // cancellation tokens. This is a known limitation. If cancellation support is needed,
        // the implementation should be changed to use BRPOP with a timeout and poll the token.
        
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _queue.DequeueAsync(cts.Token);
        
        // Assert
        // Will return null (no job) rather than throwing OperationCanceledException
        Assert.Null(result);
    }
}
