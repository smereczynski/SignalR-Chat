using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Resilience
{
    /// <summary>
    /// Lightweight async retry helper with exponential backoff and jitter.
    /// Avoids external deps (Polly) while covering common transient fault handling.
    /// </summary>
    public static class RetryHelper
    {
        public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, Func<Exception, bool> isTransient, ILogger logger, string operationName, int maxAttempts = 3, int baseDelayMs = 500, int perAttemptTimeoutMs = 5000)
        {
            var rnd = new Random();
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var cts = new CancellationTokenSource(perAttemptTimeoutMs);
                try
                {
                    return await action(cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (isTransient(ex) && attempt < maxAttempts)
                {
                    var backoff = (int)(baseDelayMs * Math.Pow(2, attempt - 1));
                    var jitter = rnd.Next(0, baseDelayMs);
                    var delay = TimeSpan.FromMilliseconds(backoff + jitter);
                    logger?.LogWarning(ex, "Transient failure in {Operation} attempt {Attempt}/{Max}. Retrying in {DelayMs}ms", operationName, attempt, maxAttempts, delay.TotalMilliseconds);
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
            // Final attempt without catch to surface exception
            using (var finalCts = new CancellationTokenSource(perAttemptTimeoutMs))
            {
                return await action(finalCts.Token).ConfigureAwait(false);
            }
        }
    }
}
