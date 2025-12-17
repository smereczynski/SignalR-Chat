using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Controllers;
using Chat.Web.Models;
using Chat.Web.Options;
using Chat.Web.Repositories;
using Chat.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Chat.Tests;

/// <summary>
/// Unit tests for manual retry functionality (Phase 4).
/// Tests cover REST endpoint for retrying failed translations.
/// 
/// Note: SignalR hub tests would require complex mocking of Hub infrastructure
/// and are better suited for integration tests. These tests focus on the
/// REST API which is more easily unit-testable.
/// </summary>
public class ManualRetryTests
{
    private readonly Mock<IMessagesRepository> _mockMessages;
    private readonly Mock<IRoomsRepository> _mockRooms;
    private readonly Mock<IUsersRepository> _mockUsers;
    private readonly Mock<IHubContext<Chat.Web.Hubs.ChatHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<ITranslationJobQueue> _mockQueue;
    private readonly Mock<ILogger<MessagesController>> _mockControllerLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly TranslationOptions _translationOptions;

    public ManualRetryTests()
    {
        _mockMessages = new Mock<IMessagesRepository>();
        _mockRooms = new Mock<IRoomsRepository>();
        _mockUsers = new Mock<IUsersRepository>();
        _mockHubContext = new Mock<IHubContext<Chat.Web.Hubs.ChatHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockQueue = new Mock<ITranslationJobQueue>();
        _mockControllerLogger = new Mock<ILogger<MessagesController>>();
        _mockConfiguration = new Mock<IConfiguration>();

        // Setup HubContext to return mocked clients
        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        _translationOptions = new TranslationOptions
        {
            Enabled = true,
            QueueName = "translation:queue",
            MaxRetries = 3
        };
    }

    #region REST Endpoint Tests

    [Fact]
    public async Task RetryTranslation_WithTranslationDisabled_ReturnsBadRequest()
    {
        // Arrange
        var disabledOptions = new TranslationOptions { Enabled = false };
        var controller = CreateController(disabledOptions);

        // Act
        var result = await controller.RetryTranslation(123);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task RetryTranslation_WithNonExistentMessage_ReturnsNotFound()
    {
        // Arrange
        _mockMessages.Setup(m => m.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Message?)null);

        var controller = CreateController(_translationOptions);
        SetupUserContext(controller, "user1", "general");

        // Act
        var result = await controller.RetryTranslation(999);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task RetryTranslation_WithUnauthorizedUser_ReturnsForbidden()
    {
        // Arrange
        var room = new Room { Id = 1, Name = "general" };
        var user = new ApplicationUser
        {
            UserName = "user1",
            FixedRooms = new List<string> { "different-room" } // User not in general
        };
        
        var message = new Message
        {
            Id = 123,
            ToRoomId = 1,
            ToRoom = room,
            Content = "Test message",
            TranslationStatus = TranslationStatus.Failed
        };

        _mockMessages.Setup(m => m.GetByIdAsync(123))
            .ReturnsAsync(message);
        
        _mockUsers.Setup(u => u.GetByUserNameAsync("user1"))
            .ReturnsAsync(user);

        var controller = CreateController(_translationOptions);
        SetupUserContext(controller, "user1", "different-room");

        // Act
        var result = await controller.RetryTranslation(123);

        // Assert
        var forbidResult = Assert.IsType<ForbidResult>(result);
        Assert.NotNull(forbidResult);
    }

    [Fact]
    public async Task RetryTranslation_WithNonFailedStatus_ReturnsBadRequest()
    {
        // Arrange
        var room = new Room { Id = 1, Name = "general" };
        var user = new ApplicationUser
        {
            UserName = "user1",
            FixedRooms = new List<string> { "general" }
        };
        
        var message = new Message
        {
            Id = 123,
            ToRoomId = 1,
            ToRoom = room,
            Content = "Test message",
            TranslationStatus = TranslationStatus.Pending // Not Failed
        };

        _mockMessages.Setup(m => m.GetByIdAsync(123))
            .ReturnsAsync(message);
        
        _mockUsers.Setup(u => u.GetByUserNameAsync("user1"))
            .ReturnsAsync(user);

        var controller = CreateController(_translationOptions);
        SetupUserContext(controller, "user1", "general");

        // Act
        var result = await controller.RetryTranslation(123);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task RetryTranslation_WithValidRequest_RequeuesToFrontAndReturnsSuccess()
    {
        // Arrange
        var room = new Room { Id = 1, Name = "general" };
        var user = new ApplicationUser
        {
            UserName = "user1",
            FixedRooms = new List<string> { "general" }
        };
        
        var message = new Message
        {
            Id = 123,
            ToRoomId = 1,
            ToRoom = room,
            Content = "Test message",
            TranslationStatus = TranslationStatus.Failed,
            TranslationJobId = "old-job-id"
        };

        _mockMessages.Setup(m => m.GetByIdAsync(123))
            .ReturnsAsync(message);
        
        _mockUsers.Setup(u => u.GetByUserNameAsync("user1"))
            .ReturnsAsync(user);

        _mockMessages.Setup(m => m.UpdateTranslationAsync(
            It.IsAny<int>(),
            It.IsAny<TranslationStatus>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<DateTime?>(),
            It.IsAny<Chat.Web.Models.TranslationFailureCategory?>(),
            It.IsAny<Chat.Web.Models.TranslationFailureCode?>(),
            It.IsAny<string>()))
            .ReturnsAsync(message);

        MessageTranslationJob? capturedJob = null;
        bool? capturedHighPriority = null;

        _mockQueue.Setup(q => q.RequeueAsync(It.IsAny<MessageTranslationJob>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<MessageTranslationJob, bool, CancellationToken>((job, highPriority, ct) =>
            {
                capturedJob = job;
                capturedHighPriority = highPriority;
            })
            .Returns(Task.CompletedTask);

        var controller = CreateController(_translationOptions);
        SetupUserContext(controller, "user1", "general");

        // Act
        var result = await controller.RetryTranslation(123);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        // Verify high priority requeue
        Assert.NotNull(capturedJob);
        Assert.NotNull(capturedHighPriority);
        Assert.True(capturedHighPriority.Value);
        Assert.Equal(10, capturedJob.Priority); // High priority
        Assert.Equal(0, capturedJob.RetryCount); // Reset retry count
        Assert.Equal(123, capturedJob.MessageId);
        Assert.Equal("general", capturedJob.RoomName);

        // Verify status update
        _mockMessages.Verify(m => m.UpdateTranslationAsync(
            It.Is<int>(i => i == 123),
            It.Is<TranslationStatus>(s => s == TranslationStatus.Pending),
            It.IsAny<Dictionary<string, string>>(),
            It.Is<string>(s => !string.IsNullOrEmpty(s)),
            It.Is<DateTime?>(d => !d.HasValue),
            It.IsAny<Chat.Web.Models.TranslationFailureCategory?>(),
            It.IsAny<Chat.Web.Models.TranslationFailureCode?>(),
            It.IsAny<string>()), Times.Once);

        // Verify queue requeue
        _mockQueue.Verify(q => q.RequeueAsync(
            It.IsAny<MessageTranslationJob>(),
            It.Is<bool>(b => b),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private MessagesController CreateController(TranslationOptions options)
    {
        return new MessagesController(
            _mockMessages.Object,
            _mockRooms.Object,
            _mockUsers.Object,
            _mockHubContext.Object,
            _mockControllerLogger.Object,
            _mockQueue.Object,
            Options.Create(options));
    }

    private void SetupUserContext(MessagesController controller, string userId, string roomName)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, userId),
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            }
        };

        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["CurrentRoom"] = roomName;
    }

    #endregion
}
