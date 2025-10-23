using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.ViewModels;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Chat.Web.Services
{
    /// <summary>
    /// Simplified Redis-backed presence tracker using a single hash for all users.
    /// Stores userName → user data (fullName, avatar, currentRoom) with TTL-based cleanup.
    /// </summary>
    public class RedisPresenceTracker : IPresenceTracker
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisPresenceTracker> _logger;
        private const string PresenceHashKey = "presence:users";
        private const int PresenceTtlSeconds = 600; // 10 minutes

        public RedisPresenceTracker(IConnectionMultiplexer redis, ILogger<RedisPresenceTracker> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task SetUserRoomAsync(string userName, string fullName, string avatar, string roomName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return;

            try
            {
                var db = _redis.GetDatabase();
                var user = new UserViewModel
                {
                    UserName = userName,
                    FullName = fullName ?? userName,
                    Avatar = avatar,
                    CurrentRoom = roomName ?? string.Empty
                };

                var json = JsonSerializer.Serialize(user);
                await db.HashSetAsync(PresenceHashKey, userName, json);
                await db.KeyExpireAsync(PresenceHashKey, TimeSpan.FromSeconds(PresenceTtlSeconds));
                _logger.LogDebug("Redis: User {User} set to room {Room}", userName, roomName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set user room in Redis: {User} -> {Room}", userName, roomName);
            }
        }

        public async Task<UserViewModel> GetUserAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return null;

            try
            {
                var db = _redis.GetDatabase();
                var json = await db.HashGetAsync(PresenceHashKey, userName);
                
                if (!json.HasValue)
                    return null;

                return JsonSerializer.Deserialize<UserViewModel>(json.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user from Redis: {User}", userName);
                return null;
            }
        }

        public async Task RemoveUserAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return;

            try
            {
                var db = _redis.GetDatabase();
                await db.HashDeleteAsync(PresenceHashKey, userName);
                _logger.LogDebug("Redis: User {User} removed", userName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove user from Redis: {User}", userName);
            }
        }

        public async Task<IReadOnlyList<UserViewModel>> GetAllUsersAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var entries = await db.HashGetAllAsync(PresenceHashKey);

                if (entries.Length == 0)
                    return Array.Empty<UserViewModel>();

                var users = new List<UserViewModel>();
                foreach (var entry in entries)
                {
                    try
                    {
                        var user = JsonSerializer.Deserialize<UserViewModel>(entry.Value.ToString());
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

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all users from Redis");
                return Array.Empty<UserViewModel>();
            }
        }
    }
}
