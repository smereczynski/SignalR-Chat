using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Chat.Web.Observability;
using Chat.Web.Services;

#nullable enable

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("api/telemetry")]    
    /// <summary>
    /// Accepts client-emitted telemetry events (currently reconnect attempts) and converts them into Activities + metrics.
    /// Extends observability for scenarios not automatically instrumented by SignalR.
    /// </summary>
    public class TelemetryController : ControllerBase
    {
        private readonly IInProcessMetrics _metrics;
        public TelemetryController(IInProcessMetrics metrics) => _metrics = metrics;
        /// <summary>
        /// DTO posted by the client on each reconnect attempt.
        /// </summary>
        /// <param name="Attempt">Incremental attempt number (1-based).</param>
        /// <param name="DelayMs">Delay applied before attempting reconnect (backoff).</param>
        /// <param name="ErrorCategory">Optional classification of last failure (auth|timeout|transport|server|other|unknown).</param>
        /// <param name="ErrorMessage">Optional truncated error message for diagnostics (server filtered to max length).</param>
        public record ReconnectAttemptDto(int Attempt, int DelayMs, string? ErrorCategory, string? ErrorMessage);

        /// <summary>
        /// Records a reconnect attempt as an Activity (span) with semantic tags and increments domain metrics.
        /// </summary>
        [HttpPost("reconnect")]        
        public IActionResult Reconnect([FromBody] ReconnectAttemptDto dto)
        {
            using var activity = Tracing.ActivitySource.StartActivity("Client.ReconnectAttempt");
            activity?.SetTag("reconnect.attempt", dto.Attempt);
            activity?.SetTag("reconnect.delay_ms", dto.DelayMs);
            if(!string.IsNullOrWhiteSpace(dto.ErrorCategory))
                activity?.SetTag("reconnect.error.category", dto.ErrorCategory);
            if(!string.IsNullOrWhiteSpace(dto.ErrorMessage))
            {
                var msg = dto.ErrorMessage.Length > 180 ? dto.ErrorMessage.Substring(0,180) + "…" : dto.ErrorMessage;
                activity?.SetTag("reconnect.error.message", msg);
            }
            _metrics.IncReconnectAttempt(dto.Attempt, dto.DelayMs);
            return Accepted();
        }
    }
}
