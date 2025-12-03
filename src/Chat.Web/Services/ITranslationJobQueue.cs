#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Services;

/// <summary>
/// Abstraction for translation job queue operations using Redis List.
/// Thread-safe and multi-instance safe using Redis atomic operations.
/// Uses LPUSH (enqueue) and BRPOP (dequeue) for FIFO queue semantics.
/// </summary>
public interface ITranslationJobQueue
{
    /// <summary>
    /// Enqueues a translation job to the queue (LPUSH).
    /// Returns the job ID for tracking.
    /// </summary>
    /// <param name="job">Translation job to enqueue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Job ID</returns>
    Task<string> EnqueueAsync(MessageTranslationJob job, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Dequeues a translation job from the queue (BRPOP with timeout).
    /// Blocks until a job is available or timeout expires.
    /// Returns null if queue is empty after timeout.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dequeued job or null if timeout</returns>
    Task<MessageTranslationJob?> DequeueAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Re-enqueues a job (for retries or manual retry).
    /// High priority jobs are added to the front of the queue.
    /// </summary>
    /// <param name="job">Job to re-enqueue</param>
    /// <param name="highPriority">If true, adds to front of queue (RPUSH)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RequeueAsync(MessageTranslationJob job, bool highPriority = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current queue length (number of pending jobs).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queue length</returns>
    Task<long> GetQueueLengthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a specific job from the queue by job ID.
    /// Returns true if job was found and removed, false otherwise.
    /// </summary>
    /// <param name="jobId">Job ID to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if removed, false if not found</returns>
    Task<bool> RemoveJobAsync(string jobId, CancellationToken cancellationToken = default);
}
