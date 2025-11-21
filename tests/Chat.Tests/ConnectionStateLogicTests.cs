using System;
using Xunit;

namespace Chat.Tests
{
    /// <summary>
    /// Tests for computeConnectionState() logic from chat.js.
    /// This simulates the JavaScript logic to validate connection state transitions.
    /// </summary>
    public class ConnectionStateLogicTests
    {
        /// <summary>
        /// Simulates the computeConnectionState() function from chat.js.
        /// Returns: 'connected', 'reconnecting', 'degraded', or 'disconnected'.
        /// </summary>
        private string ComputeConnectionState(
            bool isReconnecting,
            string? reconnectSource,
            long stateAge,
            string? hubState,
            bool isBackendHealthy,
            long healthCheckAge)
        {
            // Trust tracked reconnection state (within last 60 seconds)
            if (isReconnecting && reconnectSource == "automatic" && stateAge < 60000)
            {
                return "reconnecting";
            }

            // Manual reconnect: show reconnecting for first 10s, then disconnected
            if (isReconnecting && reconnectSource == "manual")
            {
                if (stateAge < 10000)
                {
                    return "reconnecting";
                }
                else
                {
                    return "disconnected";
                }
            }

            // Check SignalR connection state
            var isSignalRConnected = hubState?.ToLower() == "connected";

            // Degraded state: SignalR connected but backend unhealthy
            if (isSignalRConnected && healthCheckAge < 30000 && !isBackendHealthy)
            {
                return "degraded";
            }

            // Trust recent event-driven state (within last 5 seconds)
            if (stateAge < 5000)
            {
                if (isSignalRConnected) return "connected";
                return "disconnected";
            }

            // Fall back to polling hub state for stale/missed events
            var stateStr = hubState?.ToLower() ?? "";
            if (stateStr == "connected") return "connected";
            if (stateStr == "connecting") return "reconnecting";
            if (stateStr == "reconnecting") return "reconnecting";
            return "disconnected";
        }

        [Fact]
        public void AutomaticReconnect_WithinGracePeriod_ReturnsReconnecting()
        {
            var result = ComputeConnectionState(
                isReconnecting: true,
                reconnectSource: "automatic",
                stateAge: 5000,        // 5 seconds ago
                hubState: "reconnecting",
                isBackendHealthy: true,
                healthCheckAge: 10000
            );

            Assert.Equal("reconnecting", result);
        }

        [Fact]
        public void AutomaticReconnect_BeyondGracePeriod_FallsBackToHubState()
        {
            var result = ComputeConnectionState(
                isReconnecting: true,
                reconnectSource: "automatic",
                stateAge: 70000,       // 70 seconds ago (beyond 60s grace)
                hubState: "disconnected",
                isBackendHealthy: true,
                healthCheckAge: 10000
            );

            Assert.Equal("disconnected", result);
        }

        [Fact]
        public void ManualReconnect_First10Seconds_ReturnsReconnecting()
        {
            var result = ComputeConnectionState(
                isReconnecting: true,
                reconnectSource: "manual",
                stateAge: 5000,        // 5 seconds ago
                hubState: "disconnected",
                isBackendHealthy: false,
                healthCheckAge: 5000
            );

            Assert.Equal("reconnecting", result);
        }

        [Fact]
        public void ManualReconnect_After10Seconds_ReturnsDisconnected()
        {
            var result = ComputeConnectionState(
                isReconnecting: true,
                reconnectSource: "manual",
                stateAge: 15000,       // 15 seconds ago (backend still down)
                hubState: "disconnected",
                isBackendHealthy: false,
                healthCheckAge: 5000
            );

            Assert.Equal("disconnected", result);
        }

        [Fact]
        public void SignalRConnected_BackendUnhealthy_ReturnsDegraded()
        {
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,
                hubState: "connected",
                isBackendHealthy: false,
                healthCheckAge: 5000   // Recent health check (< 30s)
            );

            Assert.Equal("degraded", result);
        }

        [Fact]
        public void SignalRConnected_BackendHealthy_ReturnsConnected()
        {
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,
                hubState: "connected",
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal("connected", result);
        }

        [Fact]
        public void SignalRConnected_StaleHealthCheck_ReturnsConnected()
        {
            // Health check is stale (>30s), so degraded state is not triggered
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,
                hubState: "connected",
                isBackendHealthy: false,
                healthCheckAge: 35000  // 35 seconds ago (stale)
            );

            Assert.Equal("connected", result);
        }

        [Fact]
        public void RecentStateUpdate_WithinGracePeriod_TrustsEventState()
        {
            // State was updated 3 seconds ago (< 5s), trust the event-driven state
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 3000,        // 3 seconds ago
                hubState: "connected",
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal("connected", result);
        }

        [Fact]
        public void StaleState_FallsBackToHubPolling()
        {
            // State is stale (10s ago), fall back to polling hub.state
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,       // 10 seconds ago (> 5s)
                hubState: "reconnecting",
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal("reconnecting", result);
        }

        [Fact]
        public void HubDisconnected_NoReconnectFlag_ReturnsDisconnected()
        {
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,
                hubState: "disconnected",
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal("disconnected", result);
        }

        [Fact]
        public void HubConnecting_ReturnsReconnecting()
        {
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,
                hubState: "connecting",
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal("reconnecting", result);
        }

        [Fact]
        public void NullHubState_ReturnsDisconnected()
        {
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,
                hubState: null,
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal("disconnected", result);
        }

        [Theory]
        [InlineData("connected", "connected")]
        [InlineData("Connected", "connected")]
        [InlineData("CONNECTED", "connected")]
        [InlineData("reconnecting", "reconnecting")]
        [InlineData("Reconnecting", "reconnecting")]
        [InlineData("connecting", "reconnecting")]
        [InlineData("Connecting", "reconnecting")]
        [InlineData("disconnected", "disconnected")]
        [InlineData("Disconnected", "disconnected")]
        [InlineData("unknown", "disconnected")]
        public void HubState_CaseInsensitive_ReturnsCorrectState(string hubState, string expected)
        {
            var result = ComputeConnectionState(
                isReconnecting: false,
                reconnectSource: null,
                stateAge: 10000,       // Use polling fallback
                hubState: hubState,
                isBackendHealthy: true,
                healthCheckAge: 5000
            );

            Assert.Equal(expected, result);
        }
    }
}
