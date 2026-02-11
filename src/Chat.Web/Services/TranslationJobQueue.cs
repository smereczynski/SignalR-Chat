#nullable enable

using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Observability;
using Chat.Web.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chat.Web.Services;

/// <summary>
/// Redis List-based translation job queue implementation.
/// Uses LPUSH for enqueue (add to head) and BRPOP for dequeue (remove from tail) for FIFO semantics.
/// Thread-safe and multi-instance safe using Redis atomic operations.
/// </summary>
public class TranslationJobQueue : ITranslationJobQueue
{
    private readonly IDatabase? _redis;
    private readonly TranslationOptions _options;
    private readonly ILogger<TranslationJobQueue> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _isEnabled;

    public TranslationJobQueue(
        IConnectionMultiplexer? redis,
        IOptions<TranslationOptions> options,
        ILogger<TranslationJobQueue> logger)
    {
        _redis = redis?.GetDatabase();
        _options = options.Value;
        _logger = logger;
        _isEnabled = _options.Enabled && _redis != null;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        if (!_isEnabled && _options.Enabled)
        {
            _logger.LogWarning("Translation queue is disabled because Redis connection is not available");
        }
    }

    public async Task<string> EnqueueAsync(MessageTranslationJob job, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Translation queue is disabled, cannot enqueue job for message {MessageId}", job.MessageId);
            throw new InvalidOperationException("Translation queue is not available");
        }

        using var activity = Tracing.ActivitySource.StartActivity("translation.queue.enqueue");
        activity?.SetTag("job.messageId", job.MessageId);
        activity?.SetTag("job.priority", job.Priority);
        activity?.SetTag("job.retryCount", job.RetryCount);

        try
        {
            var json = JsonSerializer.Serialize(job, _jsonOptions);
            var queueKey = _options.QueueName;

            // LPUSH adds to head of list (FIFO when using BRPOP from tail)
            var length = await _redis!.ListLeftPushAsync(queueKey, json).ConfigureAwait(false);

            activity?.SetTag("queue.length", length);
            _logger.LogInformation(
                "Enqueued translation job {JobId} for message {MessageId} in room {Room} (queue length: {Length})",
                job.JobId, job.MessageId, job.RoomName, length);

            return job.JobId;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to enqueue translation job {JobId} for message {MessageId}", job.JobId, job.MessageId);
            throw new InvalidOperationException($"Failed to enqueue translation job {job.JobId} for message {job.MessageId}", ex);
        }
    }

    public async Task<MessageTranslationJob?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return null;
        }

        Activity? activity = null;
        try
        {
            var queueKey = _options.QueueName;

            // BRPOP removes from tail (blocking, returns null if timeout)
            // Note: StackExchange.Redis doesn't have true BRPOP, so we use RPOP with manual retry
            var result = await _redis!.ListRightPopAsync(queueKey).ConfigureAwait(false);

            if (result.IsNullOrEmpty)
            {
                // No tracing for empty queue polls - generates excessive telemetry with minimal value
                return null;
            }

            // Only create activity span when we actually have a job to process
            activity = Tracing.ActivitySource.StartActivity("translation.queue.dequeue");

            var jobJson = (string)result!;
            var job = JsonSerializer.Deserialize<MessageTranslationJob>(jobJson, _jsonOptions);
            if (job == null)
            {
                _logger.LogWarning("Deserialized job is null");
                return null;
            }

            activity?.SetTag("job.messageId", job.MessageId);
            activity?.SetTag("job.retryCount", job.RetryCount);
            _logger.LogDebug(
                "Dequeued translation job {JobId} for message {MessageId}",
                job.JobId, job.MessageId);

            return job;
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to deserialize translation job from queue");
            return null;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to dequeue translation job");
            throw new InvalidOperationException("Failed to dequeue translation job from Redis queue", ex);
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public async Task RequeueAsync(MessageTranslationJob job, bool highPriority = false, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Translation queue is disabled, cannot requeue job for message {MessageId}", job.MessageId);
            throw new InvalidOperationException("Translation queue is not available");
        }

        using var activity = Tracing.ActivitySource.StartActivity("translation.queue.requeue");
        activity?.SetTag("job.messageId", job.MessageId);
        activity?.SetTag("job.priority", job.Priority);
        activity?.SetTag("job.highPriority", highPriority);
        activity?.SetTag("job.retryCount", job.RetryCount);

        try
        {
            var json = JsonSerializer.Serialize(job, _jsonOptions);
            var queueKey = _options.QueueName;

            long length = highPriority
                // RPUSH adds to tail (will be dequeued first with BRPOP)
                ? await _redis!.ListRightPushAsync(queueKey, json).ConfigureAwait(false)
                // LPUSH adds to head (normal priority)
                : await _redis!.ListLeftPushAsync(queueKey, json).ConfigureAwait(false);

            activity?.SetTag("queue.length", length);
            _logger.LogInformation(
                "Re-enqueued translation job {JobId} for message {MessageId} (high priority: {HighPriority}, retry: {RetryCount}, queue length: {Length})",
                job.JobId, job.MessageId, highPriority, job.RetryCount, length);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to requeue translation job {JobId} for message {MessageId}", job.JobId, job.MessageId);
            throw new InvalidOperationException($"Failed to requeue translation job {job.JobId} for message {job.MessageId}", ex);
        }
    }

    public async Task<long> GetQueueLengthAsync(CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return 0;
        }

        try
        {
            var queueKey = _options.QueueName;
            var length = await _redis!.ListLengthAsync(queueKey).ConfigureAwait(false);
            return length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue length");
            return 0;
        }
    }

    public async Task<bool> RemoveJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return false;
        }

        using var activity = Tracing.ActivitySource.StartActivity("translation.queue.remove");
        activity?.SetTag("job.jobId", jobId);

        try
        {
            var queueKey = _options.QueueName;

            // Get all jobs from queue
            var jobs = await _redis!.ListRangeAsync(queueKey).ConfigureAwait(false);

            foreach (var jobJson in jobs)
            {
                if (jobJson.IsNullOrEmpty) continue;

                var jobPayload = (string)jobJson!;
                var job = JsonSerializer.Deserialize<MessageTranslationJob>(jobPayload, _jsonOptions);
                if (job?.JobId == jobId)
                {
                    // Remove this specific job
                    var removed = await _redis.ListRemoveAsync(queueKey, jobJson).ConfigureAwait(false);
                    if (removed > 0)
                    {
                        _logger.LogInformation("Removed translation job {JobId} from queue", jobId);
                        return true;
                    }
                }
            }

            activity?.SetTag("job.found", false);
            _logger.LogWarning("Translation job {JobId} not found in queue", jobId);
            return false;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to remove translation job {JobId}", jobId);
            return false;
        }
    }
}
