using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Azure.Cosmos;
using StackExchange.Redis;

namespace Chat.Web.Resilience
{
    /// <summary>
    /// Centralized helpers to classify transient failures for various dependencies.
    /// </summary>
    public static class Transient
    {
        public static bool IsCosmosTransient(Exception ex)
        {
            if (ex is CosmosException cex)
            {
                var code = (int)cex.StatusCode;
                if (code == 429 || code == 408 || code == 503) return true;
            }
            return IsNetworkTransient(ex);
        }

        public static bool IsRedisTransient(Exception ex)
        {
            if (ex is RedisConnectionException) return true;
            if (ex is RedisTimeoutException) return true;
            return IsNetworkTransient(ex);
        }

        public static bool IsNetworkTransient(Exception ex)
        {
            if (ex is TimeoutException) return true;
            if (ex is SocketException) return true;
            return false;
        }

        public static bool IsHttpTransient(Exception ex)
        {
            if (ex is System.Net.Http.HttpRequestException httpEx)
            {
                // Retry on network errors, timeouts, 429 (rate limit), 503 (service unavailable)
                if (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests) return true;
                if (httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable) return true;
                if (httpEx.StatusCode == System.Net.HttpStatusCode.RequestTimeout) return true;
                if (httpEx.StatusCode == System.Net.HttpStatusCode.GatewayTimeout) return true;
            }
            return IsNetworkTransient(ex);
        }
    }
}
