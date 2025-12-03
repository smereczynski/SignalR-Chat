#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Hubs;
using Chat.Web.Models;
using Chat.Web.Observability;
using Chat.Web.Options;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Web.Services;

/// <summary>
/// Background service that continuously processes translation jobs from Redis queue.
/// - Dequeues jobs with blocking timeout
/// - Processes translations in parallel (controlled by semaphore)
/// - Updates message status in Cosmos DB
/// - Broadcasts translation results via SignalR
/// - Handles failures with retry logic
/// - Implements graceful shutdown
/// </summary>
public class TranslationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationBackgroundService> _logger;
    private SemaphoreSlim? _semaphore;
    
    private ITranslationJobQueue? _queue;
    private ITranslationService? _translator;
    private IMessagesRepository? _messages;
    private IHubContext<ChatHub>? _hubContext;
    private TranslationOptions? _options;
    private bool _isEnabled;

    public TranslationBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<TranslationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resolve dependencies lazily to avoid CosmosClients initialization timing issue
        _queue = _serviceProvider.GetRequiredService<ITranslationJobQueue>();
        _translator = _serviceProvider.GetRequiredService<ITranslationService>();
        _messages = _serviceProvider.GetRequiredService<IMessagesRepository>();
        _hubContext = _serviceProvider.GetRequiredService<IHubContext<ChatHub>>();
        _options = _serviceProvider.GetRequiredService<IOptions<TranslationOptions>>().Value;
        _isEnabled = _options!.Enabled;
        _semaphore = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);

        if (!_isEnabled)
        {
            _logger.LogInformation("Translation background service is disabled (Translation:Enabled=false)");
            return;
        }

        _logger.LogInformation(
            "Translation background service started (MaxConcurrentJobs: {MaxConcurrent}, QueueName: {QueueName})",
            _options.MaxConcurrentJobs, _options.QueueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Dequeue job (blocking with timeout)
                var job = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);

                if (job == null)
                {
                    // Timeout - no jobs available, continue loop
                    continue;
                }

                // Acquire semaphore for concurrency control
                await _semaphore.WaitAsync(stoppingToken).ConfigureAwait(false);

                // Process job in background (fire-and-forget with error handling)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception in translation job processing for message {MessageId}", job.MessageId);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                _logger.LogInformation("Translation background service is shutting down");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in translation background service loop");
                // Backoff on error to avoid tight loop
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }

        // Wait for all in-flight jobs to complete (max 30 seconds)
        var shutdownTimeout = TimeSpan.FromSeconds(30);
        var shutdownCts = new CancellationTokenSource(shutdownTimeout);
        try
        {
            _logger.LogInformation("Waiting for {Count} in-flight translation jobs to complete...", 
                _options.MaxConcurrentJobs - _semaphore.CurrentCount);
            
            // Wait for all semaphore slots to be released
            for (int i = 0; i < _options.MaxConcurrentJobs; i++)
            {
                await _semaphore.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
            }
            
            _logger.LogInformation("All translation jobs completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Shutdown timeout exceeded, some translation jobs may not have completed");
        }

        _logger.LogInformation("Translation background service stopped");
    }

    private async Task ProcessJobAsync(MessageTranslationJob job, CancellationToken cancellationToken)
    {
        using var activity = Tracing.ActivitySource.StartActivity("translation.job.process");
        activity?.SetTag("job.messageId", job.MessageId);
        activity?.SetTag("job.roomName", job.RoomName);
        activity?.SetTag("job.retryCount", job.RetryCount);
        activity?.SetTag("job.priority", job.Priority);

        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation(
                "Processing translation job {JobId} for message {MessageId} in room {Room} (attempt {Attempt}/{Max})",
                job.JobId, job.MessageId, job.RoomName, job.RetryCount + 1, _options!.MaxRetries + 1);

            // 1. Update status to InProgress
            await _messages!.UpdateTranslationAsync(
                job.MessageId,
                TranslationStatus.InProgress,
                new Dictionary<string, string>(),
                job.JobId).ConfigureAwait(false);

            // 2. Call translation API
            var request = new TranslateRequest
            {
                Text = job.Content,
                SourceLanguage = job.SourceLanguage ?? "auto",
                Targets = job.TargetLanguages.Select(lang => new TranslationTarget
                {
                    Language = lang,
                    DeploymentName = job.DeploymentName
                }).ToList()
            };

            // Use timeout for API call
            using var apiCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            apiCts.CancelAfter(TimeSpan.FromSeconds(_options!.JobTimeoutSeconds));

            var response = await _translator!.TranslateAsync(request, apiCts.Token).ConfigureAwait(false);

            // 3. Extract translations
            var translations = response.Translations
                .ToDictionary(t => t.Language, t => t.Text);

            activity?.SetTag("translation.languageCount", translations.Count);

            // 4. Update status to Completed
            var message = await _messages.UpdateTranslationAsync(
                job.MessageId,
                TranslationStatus.Completed,
                translations,
                job.JobId).ConfigureAwait(false);

            var duration = DateTime.UtcNow - startTime;
            activity?.SetTag("translation.durationMs", duration.TotalMilliseconds);

            // 5. Broadcast to room
            await _hubContext!.Clients.Group(job.RoomName)
                .SendAsync("translationCompleted", new
                {
                    messageId = job.MessageId,
                    translations,
                    detectedLanguage = response.DetectedLanguage,
                    timestamp = DateTime.UtcNow
                }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Translation completed for message {MessageId} with {Count} languages in {DurationMs}ms",
                job.MessageId, translations.Count, duration.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown requested - requeue job for next startup
            _logger.LogWarning(
                "Translation job {JobId} cancelled due to shutdown, re-enqueueing for message {MessageId}",
                job.JobId, job.MessageId);

            await _queue!.RequeueAsync(job, highPriority: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex,
                "Translation failed for message {MessageId} (attempt {Retry}/{Max}): {Error}",
                job.MessageId, job.RetryCount + 1, _options!.MaxRetries + 1, ex.Message);

            // Retry logic
            if (job.RetryCount < _options.MaxRetries)
            {
                job.RetryCount++;
                var delay = TimeSpan.FromSeconds(_options!.RetryDelaySeconds * job.RetryCount);

                _logger.LogInformation(
                    "Retrying translation for message {MessageId} in {DelaySeconds}s (attempt {Retry}/{Max})",
                    job.MessageId, delay.TotalSeconds, job.RetryCount + 1, _options.MaxRetries + 1);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await _queue!.RequeueAsync(job, highPriority: false, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Max retries exceeded - mark as failed
                _logger.LogError(
                    "Translation failed permanently for message {MessageId} after {MaxRetries} attempts",
                    job.MessageId, _options!.MaxRetries + 1);

                await _messages!.UpdateTranslationAsync(
                    job.MessageId,
                    TranslationStatus.Failed,
                    new Dictionary<string, string>(),
                    job.JobId,
                    DateTime.UtcNow).ConfigureAwait(false);

                // Broadcast failure
                await _hubContext!.Clients.Group(job.RoomName)
                    .SendAsync("translationFailed", new
                    {
                        messageId = job.MessageId,
                        error = "Translation failed after multiple attempts",
                        timestamp = DateTime.UtcNow
                    }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override void Dispose()
    {
        _semaphore?.Dispose();
        base.Dispose();
    }
}
