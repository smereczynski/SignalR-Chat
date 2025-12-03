# Asynchronous Message Translation - Implementation Plan

**Status**: Phase 1 Complete (Foundation) - Phase 2 Pending (Queue + Processing)  
**Branch**: `127-p0-implement-real-time-message-translation`  
**Last Updated**: 2025-12-03

---

## Overview

Implement asynchronous message translation to avoid blocking users while translations are processed. Messages are saved immediately to Cosmos DB and displayed to users with a "translation pending" indicator. Translation jobs are queued in Redis and processed by a background service. When complete, translations are broadcast to all room members via SignalR.

### Design Goals

1. **Non-blocking UX**: Users see their message immediately, translation appears when ready
2. **Multi-instance safe**: Redis queue ensures only one instance processes each job
3. **Resilient**: Failed translations can be manually retried
4. **Observable**: Full telemetry and logging for translation pipeline
5. **Configurable**: Queue size, concurrency, timeouts, retry policies

---

## Architecture

### Flow Diagram

```
User sends message
    ↓
ChatHub.SendMessage
    ↓
1. Save to Cosmos (status=Pending)
    ↓
2. Enqueue job to Redis List
    ↓
3. Broadcast message immediately (with pending status)
    ↓
4. Return to client (non-blocking)

--- Background Process ---

TranslationBackgroundService (continuous loop)
    ↓
1. BRPOP job from Redis queue (blocking, 5s timeout)
    ↓
2. Update message status=InProgress in Cosmos
    ↓
3. Call Azure AI Translator API
    ↓
4a. SUCCESS: Update message (status=Completed, translations={})
    ↓
4b. FAILURE: Update message (status=Failed, failedAt=now)
    ↓
5. Broadcast TranslationCompleted(messageId, translations) to room
```

### Redis Data Structures

**Queue** (List):
- Key: `translation:jobs` (or configurable via `Translation__QueueName`)
- Operations: `LPUSH` (enqueue), `BRPOP` (dequeue with blocking)
- Value: JSON-serialized `MessageTranslationJob`

**Job Metadata** (Hash) - Optional for monitoring:
- Key: `transjob:{messageId}:{timestamp}`
- Fields: `status`, `startedAt`, `completedAt`, `retryCount`

---

## Phase 1: Foundation ✅ COMPLETED

### Completed Items

- [x] **TranslationStatus enum** (`src/Chat.Web/Models/TranslationStatus.cs`)
  - Values: `None`, `Pending`, `InProgress`, `Completed`, `Failed`

- [x] **Message model updates** (`src/Chat.Web/Models/Message.cs`)
  - `TranslationStatus` field
  - `Translations` dictionary (lang → text)
  - `TranslationJobId` string
  - `TranslationFailedAt` nullable DateTime
  - `IsTranslated` computed property

- [x] **MessageTranslationJob model** (`src/Chat.Web/Models/MessageTranslationJob.cs`)
  - Fields: `MessageId`, `RoomName`, `Content`, `SourceLanguage`, `TargetLanguages`, `DeploymentName`, `CreatedAt`, `RetryCount`, `Priority`, `JobId`

- [x] **Cosmos DB schema updates** (`src/Chat.Web/Repositories/CosmosRepositories.cs`)
  - Extended `MessageDoc` with: `translationStatus`, `translations`, `translationJobId`, `translationFailedAt`
  - Updated `MapMessage` mapper
  - Updated `CreateAsync` to persist translation fields

- [x] **Repository interface** (`src/Chat.Web/Repositories/IMessagesRepository.cs`)
  - Added `UpdateTranslationAsync` method signature

- [x] **Repository implementations**
  - `CosmosMessagesRepository.UpdateTranslationAsync` - Full Cosmos DB implementation with retry logic
  - `InMemoryMessagesRepository.UpdateTranslationAsync` - In-memory implementation for testing

---

## Phase 2: Queue & Processing 🔄 IN PROGRESS

### 2.1 Translation Options Configuration

**File**: `src/Chat.Web/Options/TranslationOptions.cs`

Add to existing `TranslationOptions` class:

```csharp
/// <summary>
/// Redis queue name for translation jobs (default: "translation:jobs")
/// </summary>
public string QueueName { get; set; } = "translation:jobs";

/// <summary>
/// Maximum number of concurrent translation jobs to process (default: 5)
/// </summary>
public int MaxConcurrentJobs { get; set; } = 5;

/// <summary>
/// Timeout in seconds for translation API calls (default: 30)
/// </summary>
public int JobTimeoutSeconds { get; set; } = 30;

/// <summary>
/// Maximum number of retries for failed translations (default: 3)
/// </summary>
public int MaxRetries { get; set; } = 3;

/// <summary>
/// Delay in seconds between retries (exponential backoff base, default: 5)
/// </summary>
public int RetryDelaySeconds { get; set; } = 5;

/// <summary>
/// Dequeue blocking timeout in seconds (default: 5)
/// </summary>
public int DequeueTimeoutSeconds { get; set; } = 5;
```

**Configuration**: `src/Chat.Web/appsettings.Development.json` and `appsettings.Production.json`

```json
"Translation": {
  "Enabled": true,
  "Endpoint": "...",
  "SubscriptionKey": "...",
  "Region": "polandcentral",
  "ApiVersion": "2025-10-01-preview",
  "DeploymentName": "gpt-4o-mini",
  "CacheTtlSeconds": 3600,
  "QueueName": "translation:jobs",
  "MaxConcurrentJobs": 5,
  "JobTimeoutSeconds": 30,
  "MaxRetries": 3,
  "RetryDelaySeconds": 5,
  "DequeueTimeoutSeconds": 5
}
```

### 2.2 Translation Job Queue Interface

**File**: `src/Chat.Web/Services/ITranslationJobQueue.cs`

```csharp
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Services;

/// <summary>
/// Abstraction for translation job queue operations using Redis List.
/// Thread-safe and multi-instance safe.
/// </summary>
public interface ITranslationJobQueue
{
    /// <summary>
    /// Enqueues a translation job to the queue (LPUSH).
    /// Returns the job ID for tracking.
    /// </summary>
    Task<string> EnqueueAsync(MessageTranslationJob job, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Dequeues a translation job from the queue (BRPOP with timeout).
    /// Returns null if queue is empty after timeout.
    /// </summary>
    Task<MessageTranslationJob> DequeueAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Re-enqueues a job (for retries or manual retry).
    /// Uses higher priority if specified.
    /// </summary>
    Task RequeueAsync(MessageTranslationJob job, bool highPriority = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current queue length.
    /// </summary>
    Task<long> GetQueueLengthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a specific job from the queue by job ID.
    /// </summary>
    Task<bool> RemoveJobAsync(string jobId, CancellationToken cancellationToken = default);
}
```

### 2.3 Translation Job Queue Implementation

**File**: `src/Chat.Web/Services/TranslationJobQueue.cs`

**Key Implementation Details**:
- Use `StackExchange.Redis` `IDatabase`
- Serialize jobs with `System.Text.Json`
- Use `LPUSH` for enqueue (left push = FIFO when using BRPOP)
- Use `BRPOP` for dequeue with configurable timeout
- Add telemetry with `ActivitySource`
- Handle Redis connection failures gracefully
- Log all operations with structured logging

**Example**:

```csharp
public class TranslationJobQueue : ITranslationJobQueue
{
    private readonly IDatabase _redis;
    private readonly TranslationOptions _options;
    private readonly ILogger<TranslationJobQueue> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public async Task<string> EnqueueAsync(MessageTranslationJob job, CancellationToken cancellationToken = default)
    {
        using var activity = Tracing.ActivitySource.StartActivity("translation.queue.enqueue");
        activity?.SetTag("job.messageId", job.MessageId);
        activity?.SetTag("job.priority", job.Priority);
        
        var json = JsonSerializer.Serialize(job, _jsonOptions);
        var queueKey = _options.QueueName;
        
        // LPUSH adds to head of list (FIFO with BRPOP from tail)
        await _redis.ListLeftPushAsync(queueKey, json);
        
        _logger.LogInformation(
            "Enqueued translation job {JobId} for message {MessageId} in room {Room}",
            job.JobId, job.MessageId, job.RoomName);
        
        return job.JobId;
    }
    
    public async Task<MessageTranslationJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var queueKey = _options.QueueName;
        var timeout = TimeSpan.FromSeconds(_options.DequeueTimeoutSeconds);
        
        // BRPOP removes from tail (blocking, returns null if timeout)
        var result = await _redis.ListRightPopAsync(queueKey);
        
        if (result.IsNullOrEmpty)
            return null;
        
        var job = JsonSerializer.Deserialize<MessageTranslationJob>(result, _jsonOptions);
        
        _logger.LogDebug(
            "Dequeued translation job {JobId} for message {MessageId}",
            job.JobId, job.MessageId);
        
        return job;
    }
    
    // ... other methods
}
```

### 2.4 Translation Background Service

**File**: `src/Chat.Web/Services/TranslationBackgroundService.cs`

**Hosted Service** that continuously processes translation jobs.

**Key Features**:
- Implements `BackgroundService` (ASP.NET Core hosted service)
- Continuous loop with graceful shutdown
- Concurrency control with `SemaphoreSlim` (max 5 concurrent)
- Circuit breaker pattern for API failures (use Polly or manual)
- Updates message status in Cosmos DB
- Broadcasts translation results via SignalR `IHubContext<ChatHub>`
- Comprehensive error handling and logging

**Example**:

```csharp
public class TranslationBackgroundService : BackgroundService
{
    private readonly ITranslationJobQueue _queue;
    private readonly ITranslationService _translator;
    private readonly IMessagesRepository _messages;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly TranslationOptions _options;
    private readonly ILogger<TranslationBackgroundService> _logger;
    private readonly SemaphoreSlim _semaphore;

    public TranslationBackgroundService(/* DI */)
    {
        _semaphore = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Translation background service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Dequeue job (blocking with timeout)
                var job = await _queue.DequeueAsync(stoppingToken);
                
                if (job == null)
                    continue; // Timeout, retry
                
                // Acquire semaphore for concurrency control
                await _semaphore.WaitAsync(stoppingToken);
                
                // Process job in background (fire-and-forget with error handling)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(job, stoppingToken);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in translation background service loop");
                await Task.Delay(1000, stoppingToken); // Backoff on error
            }
        }
        
        _logger.LogInformation("Translation background service stopped");
    }
    
    private async Task ProcessJobAsync(MessageTranslationJob job, CancellationToken cancellationToken)
    {
        using var activity = Tracing.ActivitySource.StartActivity("translation.job.process");
        activity?.SetTag("job.messageId", job.MessageId);
        activity?.SetTag("job.retryCount", job.RetryCount);
        
        try
        {
            // 1. Update status to InProgress
            await _messages.UpdateTranslationAsync(
                job.MessageId, 
                TranslationStatus.InProgress, 
                new Dictionary<string, string>(),
                job.JobId);
            
            // 2. Call translation API
            var request = new TranslateRequest
            {
                Text = job.Content,
                SourceLanguage = job.SourceLanguage,
                Targets = job.TargetLanguages.Select(lang => new TranslationTarget 
                { 
                    Language = lang, 
                    DeploymentName = job.DeploymentName 
                }).ToList()
            };
            
            var response = await _translator.TranslateAsync(request, cancellationToken);
            
            // 3. Extract translations
            var translations = response.Translations
                .ToDictionary(t => t.Language, t => t.Text);
            
            // 4. Update status to Completed
            var message = await _messages.UpdateTranslationAsync(
                job.MessageId,
                TranslationStatus.Completed,
                translations,
                job.JobId);
            
            // 5. Broadcast to room
            await _hubContext.Clients.Group(job.RoomName)
                .SendAsync("translationCompleted", new
                {
                    messageId = job.MessageId,
                    translations,
                    timestamp = DateTime.UtcNow
                }, cancellationToken);
            
            _logger.LogInformation(
                "Translation completed for message {MessageId} with {Count} languages",
                job.MessageId, translations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Translation failed for message {MessageId} (attempt {Retry}/{Max})",
                job.MessageId, job.RetryCount + 1, _options.MaxRetries);
            
            // Retry logic
            if (job.RetryCount < _options.MaxRetries)
            {
                job.RetryCount++;
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds * job.RetryCount), cancellationToken);
                await _queue.RequeueAsync(job, highPriority: false, cancellationToken);
            }
            else
            {
                // Max retries exceeded - mark as failed
                await _messages.UpdateTranslationAsync(
                    job.MessageId,
                    TranslationStatus.Failed,
                    new Dictionary<string, string>(),
                    job.JobId,
                    DateTime.UtcNow);
                
                // Broadcast failure
                await _hubContext.Clients.Group(job.RoomName)
                    .SendAsync("translationFailed", new
                    {
                        messageId = job.MessageId,
                        timestamp = DateTime.UtcNow
                    }, cancellationToken);
            }
        }
    }
}
```

---

## Phase 3: ChatHub Integration 🔄 PENDING

### 3.1 Update MessageViewModel

**File**: `src/Chat.Web/ViewModels/MessageViewModel.cs`

Add fields:

```csharp
/// <summary>
/// Translation status (None, Pending, InProgress, Completed, Failed)
/// </summary>
public string TranslationStatus { get; set; }

/// <summary>
/// Translated versions of the message (key: language code, value: text)
/// </summary>
public Dictionary<string, string> Translations { get; set; }

/// <summary>
/// True if translation is completed successfully
/// </summary>
public bool IsTranslated { get; set; }
```

### 3.2 Update ChatHub.SendMessage

**File**: `src/Chat.Web/Hubs/ChatHub.cs`

Modify `SendMessage` method:

1. After `_messages.CreateAsync(msg)`:
   - Check if Translation is enabled
   - If enabled, set `msg.TranslationStatus = TranslationStatus.Pending`
   - Create `MessageTranslationJob`
   - Enqueue job: `await _translationQueue.EnqueueAsync(job)`
   - Update message in Cosmos with Pending status

2. Update `MessageViewModel` mapping to include translation fields

3. Broadcast message with translation status

**Example**:

```csharp
// After: msg = await _messages.CreateAsync(msg);

// Enqueue translation if enabled
if (_translationOptions.Value.Enabled)
{
    var job = new MessageTranslationJob
    {
        MessageId = msg.Id,
        RoomName = room.Name,
        Content = sanitized,
        SourceLanguage = "auto", // or detect from user profile
        TargetLanguages = new List<string> { "en", "pl", "de", "fr", "es" },
        DeploymentName = _translationOptions.Value.DeploymentName,
        CreatedAt = DateTime.UtcNow,
        RetryCount = 0,
        Priority = 0,
        JobId = $"transjob:{msg.Id}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
    };
    
    msg.TranslationStatus = TranslationStatus.Pending;
    msg.TranslationJobId = job.JobId;
    await _messages.UpdateTranslationAsync(msg.Id, TranslationStatus.Pending, new Dictionary<string, string>(), job.JobId);
    
    await _translationQueue.EnqueueAsync(job);
    
    _logger.LogDebug("Enqueued translation job {JobId} for message {MessageId}", job.JobId, msg.Id);
}

// Update ViewModel mapping
var vm = new ViewModels.MessageViewModel
{
    // ... existing fields ...
    TranslationStatus = msg.TranslationStatus.ToString(),
    Translations = msg.Translations ?? new Dictionary<string, string>(),
    IsTranslated = msg.IsTranslated
};
```

---

## Phase 4: Manual Retry 🔄 PENDING

### 4.1 Add Retry Endpoint to MessagesController

**File**: `src/Chat.Web/Controllers/MessagesController.cs`

```csharp
[HttpPost("{id}/retry-translation")]
[Authorize]
public async Task<IActionResult> RetryTranslation(int id)
{
    using var activity = Tracing.ActivitySource.StartActivity("api.messages.retry-translation");
    activity?.SetTag("message.id", id);
    
    var message = await _messages.GetByIdAsync(id);
    if (message == null)
        return NotFound(new { error = "Message not found" });
    
    if (message.TranslationStatus != TranslationStatus.Failed)
        return BadRequest(new { error = "Translation is not in failed state" });
    
    // Create new job with high priority
    var job = new MessageTranslationJob
    {
        MessageId = message.Id,
        RoomName = message.ToRoom.Name,
        Content = message.Content,
        SourceLanguage = "auto",
        TargetLanguages = new List<string> { "en", "pl", "de", "fr", "es" },
        DeploymentName = _translationOptions.Value.DeploymentName,
        CreatedAt = DateTime.UtcNow,
        RetryCount = 0,
        Priority = 10, // High priority for manual retries
        JobId = $"transjob:{message.Id}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
    };
    
    await _translationQueue.RequeueAsync(job, highPriority: true);
    await _messages.UpdateTranslationAsync(message.Id, TranslationStatus.Pending, new Dictionary<string, string>(), job.JobId);
    
    _logger.LogInformation("Manual retry triggered for message {MessageId} by user {User}", id, User.Identity.Name);
    
    return Ok(new { success = true, jobId = job.JobId });
}
```

### 4.2 Add Retry Hub Method to ChatHub

**File**: `src/Chat.Web/Hubs/ChatHub.cs`

```csharp
public async Task RetryTranslation(int messageId)
{
    using var activity = Tracing.ActivitySource.StartActivity("ChatHub.RetryTranslation");
    activity?.SetTag("chat.messageId", messageId);
    
    var currentRoom = Context.Items["CurrentRoom"] as string;
    if (string.IsNullOrEmpty(currentRoom))
    {
        await Clients.Caller.SendAsync("onError", _localizer["ErrorNotInRoom"].Value);
        return;
    }
    
    var message = await _messages.GetByIdAsync(messageId);
    if (message == null)
    {
        await Clients.Caller.SendAsync("onError", _localizer["ErrorMessageNotFound"].Value);
        return;
    }
    
    // Authorization: user must be in the same room
    if (message.ToRoom?.Name != currentRoom)
    {
        await Clients.Caller.SendAsync("onError", _localizer["ErrorNotAuthorizedRoom"].Value);
        return;
    }
    
    if (message.TranslationStatus != TranslationStatus.Failed)
    {
        await Clients.Caller.SendAsync("onError", "Translation is not in failed state");
        return;
    }
    
    // Re-enqueue with high priority
    var job = new MessageTranslationJob
    {
        MessageId = message.Id,
        RoomName = message.ToRoom.Name,
        Content = message.Content,
        SourceLanguage = "auto",
        TargetLanguages = new List<string> { "en", "pl", "de", "fr", "es" },
        DeploymentName = _translationOptions.Value.DeploymentName,
        CreatedAt = DateTime.UtcNow,
        RetryCount = 0,
        Priority = 10,
        JobId = $"transjob:{message.Id}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
    };
    
    await _translationQueue.RequeueAsync(job, highPriority: true);
    await _messages.UpdateTranslationAsync(message.Id, TranslationStatus.Pending, new Dictionary<string, string>(), job.JobId);
    
    // Broadcast status update to room
    await Clients.Group(currentRoom).SendAsync("translationRetrying", new
    {
        messageId,
        status = "Pending",
        timestamp = DateTime.UtcNow
    });
    
    _logger.LogInformation("Manual retry triggered for message {MessageId} by user {User}", messageId, IdentityName);
}
```

---

## Phase 5: Service Registration 🔄 PENDING

### 5.1 Update Startup.cs

**File**: `src/Chat.Web/Startup.cs`

Add to `ConfigureServices`:

```csharp
// Translation services
services.Configure<TranslationOptions>(Configuration.GetSection("Translation"));

if (translationEnabled)
{
    // Translation queue
    services.AddSingleton<ITranslationJobQueue, TranslationJobQueue>();
    
    // Background service for processing translation jobs
    services.AddHostedService<TranslationBackgroundService>();
}
```

Ensure `ITranslationService` is already registered (from earlier work).

---

## Phase 6: Testing 🔄 PENDING

### 6.1 Unit Tests

**File**: `tests/Chat.Tests/TranslationJobQueueTests.cs`

Test cases:
- Enqueue/dequeue operations
- Queue length tracking
- Re-queue with priority
- Job removal
- Serialization/deserialization

### 6.2 Integration Tests

**File**: `tests/Chat.IntegrationTests/TranslationBackgroundServiceTests.cs`

Test cases:
- Job processing end-to-end
- Concurrent job processing (multiple jobs)
- Retry logic on API failure
- Max retries exceeded (mark as failed)
- Status transitions (Pending → InProgress → Completed)
- SignalR broadcast verification

### 6.3 Manual Testing

1. Send message with translation enabled
2. Verify message appears immediately with "Pending" status
3. Wait for translation to complete (~3-5 seconds)
4. Verify translations appear in UI
5. Test manual retry for failed translations
6. Test multi-instance safety (run 2+ app instances)

---

## Configuration Management

### Environment Variables (.env.local)

```bash
Translation__Enabled=true
Translation__Endpoint=https://aif-interpres-dev-plc.cognitiveservices.azure.com
Translation__SubscriptionKey=your-key-here
Translation__Region=polandcentral
Translation__ApiVersion=2025-10-01-preview
Translation__DeploymentName=gpt-4o-mini
Translation__CacheTtlSeconds=3600
Translation__QueueName=translation:jobs
Translation__MaxConcurrentJobs=5
Translation__JobTimeoutSeconds=30
Translation__MaxRetries=3
Translation__RetryDelaySeconds=5
Translation__DequeueTimeoutSeconds=5
```

### Bicep Infrastructure (infra/bicep/modules/app-service.bicep)

Add to app settings:

```bicep
{
  name: 'Translation__QueueName'
  value: 'translation:jobs'
}
{
  name: 'Translation__MaxConcurrentJobs'
  value: '5'
}
{
  name: 'Translation__JobTimeoutSeconds'
  value: '30'
}
{
  name: 'Translation__MaxRetries'
  value: '3'
}
{
  name: 'Translation__RetryDelaySeconds'
  value: '5'
}
{
  name: 'Translation__DequeueTimeoutSeconds'
  value: '5'
}
```

---

## Monitoring & Observability

### Key Metrics

1. **Queue Length**: `translation:jobs` list length
2. **Job Processing Rate**: Jobs/second
3. **Translation Latency**: Time from enqueue to completion
4. **Failure Rate**: Failed jobs / total jobs
5. **Retry Rate**: Re-queued jobs / total jobs
6. **Active Jobs**: Jobs currently in progress

### Telemetry Tags

- `job.messageId`
- `job.retryCount`
- `job.priority`
- `translation.status`
- `translation.languageCount`
- `translation.duration`

### Log Events

- `TranslationJobEnqueued`
- `TranslationJobDequeued`
- `TranslationJobStarted`
- `TranslationJobCompleted`
- `TranslationJobFailed`
- `TranslationJobRetrying`
- `TranslationJobMaxRetriesExceeded`

---

## Security Considerations

1. **Authorization**: Only users in the room can retry translations
2. **Rate Limiting**: Consider adding rate limit to retry endpoint
3. **Input Validation**: Sanitize message content before translation
4. **PII Protection**: Never log message content (use LogSanitizer)
5. **API Key Security**: Store in Azure Key Vault (production)

---

## Performance Considerations

1. **Concurrency**: Default 5 concurrent jobs (configurable)
2. **Queue Backpressure**: Monitor queue length, scale instances if needed
3. **Cache Hit Rate**: Most translations should hit Redis cache
4. **API Quota**: Monitor Azure AI Translator quota consumption
5. **Database Load**: UpdateTranslationAsync uses partition key for efficiency

---

## Deployment Checklist

- [ ] Update configuration in all environments (dev, staging, prod)
- [ ] Verify Redis connection in all environments
- [ ] Test translation API connectivity from App Service
- [ ] Monitor queue length after deployment
- [ ] Verify background service starts successfully
- [ ] Test manual retry functionality
- [ ] Check Application Insights for telemetry
- [ ] Verify SignalR broadcasts work across instances

---

## Future Enhancements

1. **Dead Letter Queue**: Move failed jobs (max retries exceeded) to DLQ
2. **Admin Dashboard**: View queue status, failed jobs, retry statistics
3. **Language Detection**: Auto-detect source language from message
4. **User Preferences**: Allow users to select target languages
5. **Batch Processing**: Process multiple messages in single API call
6. **Priority Boost**: Boost priority for messages from active users
7. **Scheduled Retry**: Background job to retry failed translations periodically

---

## References

- Azure AI Translator API: https://learn.microsoft.com/azure/ai-services/translator/
- StackExchange.Redis Documentation: https://stackexchange.github.io/StackExchange.Redis/
- ASP.NET Core BackgroundService: https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services
- SignalR Hub Context: https://learn.microsoft.com/aspnet/core/signalr/hubcontext
