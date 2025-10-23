using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.ViewModels;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Chat.Web.Services
{
    /// <summary>
    /// Redis-backed distributed presence tracker for multi-instance SignalR deployments.
    /// Uses Redis hashes to store user presence data with TTL-based cleanup.
    /// </summary>
    public class RedisPresenceTracker : IPresenceTracker
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisPresenceTracker> _logger;
        private const string PresenceKeyPrefix = "presence:user:";
        private const string ConnectionMapKey = "presence:connections";
        private const int PresenceTtlSeconds = 300; // 5 minutes

        public RedisPresenceTracker(IConnectionMultiplexer redis, ILogger<RedisPresenceTracker> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task UserJoinedRoomAsync(string userName, string fullName, string avatar, string roomName)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(roomName))
                return;

            try
            {
                var db = _redis.GetDatabase();
                var key = PresenceKeyPrefix + userName;
                var user = new UserViewModel
                {
                    UserName = userName,
                    FullName = fullName ?? userName,
                    Avatar = avatar,
                    CurrentRoom = roomName
                };

                var json = JsonSerializer.Serialize(user);
                await db.StringSetAsync(key, json, TimeSpan.FromSeconds(PresenceTtlSeconds));
                _logger.LogDebug("Redis: User {User} joined room {Room}", userName, roomName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track user join in Redis: {User} -> {Room}", userName, roomName);
            }
        }

        public async Task UserLeftRoomAsync(string userName, string roomName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return;

            try
            {
                var db = _redis.GetDatabase();
                var key = PresenceKeyPrefix + userName;
                
                // Check if user exists and update their room to empty
                var existing = await db.StringGetAsync(key);
                if (existing.HasValue)
                {
                    var user = JsonSerializer.Deserialize<UserViewModel>(existing.ToString());
                    if (user != null && user.CurrentRoom == roomName)
                    {
                        user.CurrentRoom = string.Empty;
                        var json = JsonSerializer.Serialize(user);
                        await db.StringSetAsync(key, json, TimeSpan.FromSeconds(PresenceTtlSeconds));
                        _logger.LogDebug("Redis: User {User} left room {Room}", userName, roomName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track user leave in Redis: {User} <- {Room}", userName, roomName);
            }
        }

        public async Task UserDisconnectedAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return;

            try
            {
                var db = _redis.GetDatabase();
                var key = PresenceKeyPrefix + userName;
                await db.KeyDeleteAsync(key);
                await db.HashDeleteAsync(ConnectionMapKey, userName);
                _logger.LogDebug("Redis: User {User} disconnected", userName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove user from Redis: {User}", userName);
            }
        }

        public async Task<IReadOnlyList<UserViewModel>> GetUsersInRoomAsync(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                return Array.Empty<UserViewModel>();

            try
            {
                var db = _redis.GetDatabase();
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                var keys = server.Keys(pattern: PresenceKeyPrefix + "*").ToArray();

                if (keys.Length == 0)
                    return Array.Empty<UserViewModel>();

                var values = await db.StringGetAsync(keys);
                var users = new List<UserViewModel>();

                foreach (var value in values)
                {
                    if (value.HasValue)
                    {
                        try
                        {
                            var user = JsonSerializer.Deserialize<UserViewModel>(value.ToString());
                            if (user != null && user.CurrentRoom == roomName)
                            {
                                users.Add(user);
                            }
                        }
                        catch
                        {
                            // Skip malformed entries
                        }
                    }
                }

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get users in room from Redis: {Room}", roomName);
                return Array.Empty<UserViewModel>();
            }
        }

        public async Task<IReadOnlyList<UserViewModel>> GetAllUsersAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                var keys = server.Keys(pattern: PresenceKeyPrefix + "*").ToArray();

                if (keys.Length == 0)
                    return Array.Empty<UserViewModel>();

                var values = await db.StringGetAsync(keys);
                var users = new List<UserViewModel>();

                foreach (var value in values)
                {
                    if (value.HasValue)
                    {
                        try
                        {
                            var user = JsonSerializer.Deserialize<UserViewModel>(value.ToString());
                            if (user != null)
                            {
                                users.Add(user);
                            }
                        }
                        catch
                        {
                            // Skip malformed entries
                        }
                    }
                }

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all users from Redis");
                return Array.Empty<UserViewModel>();
            }
        }

        public async Task UpdateConnectionIdAsync(string userName, string connectionId)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(connectionId))
                return;

            try
            {
                var db = _redis.GetDatabase();
                await db.HashSetAsync(ConnectionMapKey, userName, connectionId);
                await db.KeyExpireAsync(ConnectionMapKey, TimeSpan.FromSeconds(PresenceTtlSeconds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update connection ID in Redis: {User}", userName);
            }
        }

        public async Task<string> GetConnectionIdAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return null;

            try
            {
                var db = _redis.GetDatabase();
                var connectionId = await db.HashGetAsync(ConnectionMapKey, userName);
                return connectionId.HasValue ? connectionId.ToString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get connection ID from Redis: {User}", userName);
                return null;
            }
        }
    }
}
