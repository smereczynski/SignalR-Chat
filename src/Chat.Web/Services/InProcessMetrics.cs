using System;
using System.Threading;
using System.Diagnostics.Metrics;

namespace Chat.Web.Services
{
    /// <summary>
    /// In-memory counters & gauges surfaced both through OpenTelemetry and a lightweight snapshot endpoint.
    /// Thread-safe via Interlocked; minimal aggregation logic.
    /// </summary>
    public interface IInProcessMetrics
    {
        void IncMessagesSent();
        void IncRoomsJoined();
        void IncOtpRequests();
        void IncOtpVerifications();
        void IncReconnectAttempt(int attempt, int delayMs);
        void IncActiveConnections();
        void DecActiveConnections();
        MetricsSnapshot Snapshot();
        DateTimeOffset StartTime { get; }
    }

    /// <summary>
    /// Immutable snapshot of current counters and derived uptime.
    /// </summary>
    public record MetricsSnapshot(long MessagesSent, long RoomsJoined, long OtpRequests, long OtpVerifications, long ActiveConnections, long ReconnectAttempts, TimeSpan Uptime);

    /// <summary>
    /// Default implementation of <see cref="IInProcessMetrics"/> publishing counters to both in-process state and an OpenTelemetry Meter.
    /// </summary>
    public class InProcessMetrics : IInProcessMetrics
    {
        private long _messagesSent;
        private long _roomsJoined;
        private long _otpRequests;
        private long _otpVerifications;
        private long _activeConnections;
        private long _reconnectAttempts;

        private static readonly Meter Meter = new("Chat.Web", "1.0.0");
        private static readonly Counter<long> MessagesSentCounter = Meter.CreateCounter<long>("chat.messages.sent");
        private static readonly Counter<long> RoomsJoinedCounter = Meter.CreateCounter<long>("chat.rooms.joined");
        private static readonly Counter<long> OtpRequestsCounter = Meter.CreateCounter<long>("chat.otp.requests");
        private static readonly Counter<long> OtpVerificationsCounter = Meter.CreateCounter<long>("chat.otp.verifications");
        private static readonly Counter<long> ReconnectAttemptsCounter = Meter.CreateCounter<long>("chat.reconnect.attempts");
        private static readonly UpDownCounter<long> ActiveConnectionsGauge = Meter.CreateUpDownCounter<long>("chat.connections.active");

        public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;

        public void IncMessagesSent(){ Interlocked.Increment(ref _messagesSent); MessagesSentCounter.Add(1); }
        public void IncRoomsJoined(){ Interlocked.Increment(ref _roomsJoined); RoomsJoinedCounter.Add(1); }
        public void IncOtpRequests(){ Interlocked.Increment(ref _otpRequests); OtpRequestsCounter.Add(1); }
        public void IncOtpVerifications(){ Interlocked.Increment(ref _otpVerifications); OtpVerificationsCounter.Add(1); }
        public void IncReconnectAttempt(int attempt, int delayMs){ Interlocked.Increment(ref _reconnectAttempts); ReconnectAttemptsCounter.Add(1, new ("attempt", attempt), new ("delay_ms", delayMs)); }
        public void IncActiveConnections(){ Interlocked.Increment(ref _activeConnections); ActiveConnectionsGauge.Add(1); }
        public void DecActiveConnections(){ Interlocked.Decrement(ref _activeConnections); ActiveConnectionsGauge.Add(-1); }

        public MetricsSnapshot Snapshot() => new(
            _messagesSent,
            _roomsJoined,
            _otpRequests,
            _otpVerifications,
            _activeConnections,
            _reconnectAttempts,
            DateTimeOffset.UtcNow - StartTime);
    }
}
