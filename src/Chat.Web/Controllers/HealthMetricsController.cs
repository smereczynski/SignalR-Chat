using Microsoft.AspNetCore.Mvc;
using System;
using Chat.Web.Services;

namespace Chat.Web.Controllers
{
    [ApiController]
    [Route("healthz/metrics")]
    /// <summary>
    /// Lightweight in-process metrics snapshot endpoint (side-channel separate from OpenTelemetry exporters).
    /// Used for local health checks or simple dashboards without scraping the OTel pipeline.
    /// </summary>
    public class HealthMetricsController : ControllerBase
    {
        private readonly IInProcessMetrics _metrics;
        public HealthMetricsController(IInProcessMetrics metrics) => _metrics = metrics;

        /// <summary>
        /// Returns a point-in-time metrics snapshot including uptime, counts and reconnect attempts.
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            var snap = _metrics.Snapshot();
            return Ok(new {
                uptimeSeconds = (long)snap.Uptime.TotalSeconds,
                activeConnections = snap.ActiveConnections,
                messagesSent = snap.MessagesSent,
                roomsJoined = snap.RoomsJoined,
                otpRequests = snap.OtpRequests,
                otpVerifications = snap.OtpVerifications,
                reconnectAttempts = snap.ReconnectAttempts,
                timestamp = DateTimeOffset.UtcNow
            });
        }
    }
}
