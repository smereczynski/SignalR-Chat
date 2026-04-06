using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Chat.Web.Utilities;
using Microsoft.Extensions.Logging;

namespace Chat.Web.Services
{
    public class DispatchCenterTopologyService
    {
        private readonly IDispatchCentersRepository _dispatchCenters;
        private readonly IUsersRepository _users;
        private readonly IRoomsRepository _rooms;
        private readonly ILogger<DispatchCenterTopologyService> _logger;

        public DispatchCenterTopologyService(
            IDispatchCentersRepository dispatchCenters,
            IUsersRepository users,
            IRoomsRepository rooms,
            ILogger<DispatchCenterTopologyService> logger)
        {
            _dispatchCenters = dispatchCenters;
            _users = users;
            _rooms = rooms;
            _logger = logger;
        }

        public async Task AssignUserAsync(string dispatchCenterId, string userName)
        {
            var dispatchCenter = await _dispatchCenters.GetByIdAsync(dispatchCenterId).ConfigureAwait(false);
            var user = await _users.GetByUserNameAsync(userName).ConfigureAwait(false);
            if (dispatchCenter == null || user == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(user.DispatchCenterId) &&
                !string.Equals(user.DispatchCenterId, dispatchCenterId, StringComparison.OrdinalIgnoreCase))
            {
                await RemoveUserFromDispatchCenterAsync(user.DispatchCenterId, userName).ConfigureAwait(false);
            }

            user.DispatchCenterId = dispatchCenterId;
            await _users.UpsertAsync(user).ConfigureAwait(false);
            await _dispatchCenters.AssignUserAsync(dispatchCenterId, userName).ConfigureAwait(false);
            await SyncRoomsAsync().ConfigureAwait(false);
        }

        public async Task RemoveUserFromDispatchCenterAsync(string dispatchCenterId, string userName)
        {
            if (string.IsNullOrWhiteSpace(dispatchCenterId) || string.IsNullOrWhiteSpace(userName))
            {
                return;
            }

            var user = await _users.GetByUserNameAsync(userName).ConfigureAwait(false);
            if (user != null && string.Equals(user.DispatchCenterId, dispatchCenterId, StringComparison.OrdinalIgnoreCase))
            {
                user.DispatchCenterId = null;
                await _users.UpsertAsync(user).ConfigureAwait(false);
            }

            await _dispatchCenters.UnassignUserAsync(dispatchCenterId, userName).ConfigureAwait(false);
            await SyncRoomsAsync().ConfigureAwait(false);
        }

        public async Task SaveDispatchCenterAsync(DispatchCenter dispatchCenter, IEnumerable<string> correspondingDispatchCenterIds)
        {
            if (dispatchCenter == null) throw new ArgumentNullException(nameof(dispatchCenter));

            var normalized = (correspondingDispatchCenterIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !string.Equals(x, dispatchCenter.Id, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allCenters = (await _dispatchCenters.GetAllAsync().ConfigureAwait(false)).ToList();
            var counterpartIds = new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase);

            dispatchCenter.CorrespondingDispatchCenterIds = normalized;
            await _dispatchCenters.UpsertAsync(dispatchCenter).ConfigureAwait(false);

            foreach (var other in allCenters.Where(x => !string.Equals(x.Id, dispatchCenter.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var currentPairs = new HashSet<string>(other.CorrespondingDispatchCenterIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var shouldContain = counterpartIds.Contains(other.Id);
                var changed = shouldContain ? currentPairs.Add(dispatchCenter.Id) : currentPairs.Remove(dispatchCenter.Id);
                if (!changed)
                {
                    continue;
                }

                other.CorrespondingDispatchCenterIds = currentPairs.ToList();
                await _dispatchCenters.UpsertAsync(other).ConfigureAwait(false);
            }

            await SyncRoomsAsync().ConfigureAwait(false);
        }

        public async Task DeleteDispatchCenterAsync(string dispatchCenterId)
        {
            var dispatchCenter = await _dispatchCenters.GetByIdAsync(dispatchCenterId).ConfigureAwait(false);
            if (dispatchCenter == null)
            {
                return;
            }

            var users = await _users.GetAllAsync().ConfigureAwait(false);
            foreach (var user in users.Where(x => string.Equals(x.DispatchCenterId, dispatchCenterId, StringComparison.OrdinalIgnoreCase)))
            {
                user.DispatchCenterId = null;
                await _users.UpsertAsync(user).ConfigureAwait(false);
            }

            var allCenters = (await _dispatchCenters.GetAllAsync().ConfigureAwait(false)).ToList();
            foreach (var other in allCenters.Where(x => !string.Equals(x.Id, dispatchCenterId, StringComparison.OrdinalIgnoreCase)))
            {
                var currentPairs = new HashSet<string>(other.CorrespondingDispatchCenterIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                if (!currentPairs.Remove(dispatchCenterId))
                {
                    continue;
                }

                other.CorrespondingDispatchCenterIds = currentPairs.ToList();
                await _dispatchCenters.UpsertAsync(other).ConfigureAwait(false);
            }

            await _dispatchCenters.DeleteAsync(dispatchCenterId).ConfigureAwait(false);
            await SyncRoomsAsync().ConfigureAwait(false);
        }

        public async Task SyncRoomsAsync()
        {
            var allCenters = (await _dispatchCenters.GetAllAsync().ConfigureAwait(false)).ToList();
            var allUsers = (await _users.GetAllAsync().ConfigureAwait(false)).ToList();
            var allRooms = (await _rooms.GetAllAsync().ConfigureAwait(false)).ToList();
            var activePairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var centerById = allCenters.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var dispatchCenter in allCenters)
            {
                foreach (var otherId in dispatchCenter.CorrespondingDispatchCenterIds ?? Array.Empty<string>())
                {
                    if (!centerById.TryGetValue(otherId, out var otherCenter))
                    {
                        continue;
                    }

                    var pairKey = DispatchCenterPairing.BuildPairKey(dispatchCenter.Id, otherCenter.Id);
                    if (!activePairKeys.Add(pairKey))
                    {
                        continue;
                    }

                    var orderedIds = pairKey.Split("::", StringSplitOptions.None);
                    var leftCenter = centerById[orderedIds[0]];
                    var rightCenter = centerById[orderedIds[1]];
                    var roomUsers = allUsers
                        .Where(x =>
                            string.Equals(x.DispatchCenterId, leftCenter.Id, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.DispatchCenterId, rightCenter.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.UserName)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var roomLanguages = allUsers
                        .Where(x =>
                            string.Equals(x.DispatchCenterId, leftCenter.Id, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.DispatchCenterId, rightCenter.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(x => LanguageCode.NormalizeToLanguageCode(x.PreferredLanguage))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var room = allRooms.FirstOrDefault(x => string.Equals(x.PairKey, pairKey, StringComparison.OrdinalIgnoreCase))
                        ?? new Room();
                    room.Name = DispatchCenterPairing.BuildRoomName(pairKey);
                    room.DisplayName = DispatchCenterPairing.BuildRoomDisplayName(leftCenter, rightCenter);
                    room.RoomType = RoomType.DispatchCenterPair;
                    room.PairKey = pairKey;
                    room.DispatchCenterAId = leftCenter.Id;
                    room.DispatchCenterBId = rightCenter.Id;
                    room.IsActive = (leftCenter.OfficerUserNames?.Count ?? 0) > 0 && (rightCenter.OfficerUserNames?.Count ?? 0) > 0;
                    room.Users = roomUsers;
                    room.Languages = roomLanguages;

                    await _rooms.UpsertAsync(room).ConfigureAwait(false);
                }
            }

            foreach (var room in allRooms.Where(x => x.RoomType == RoomType.DispatchCenterPair))
            {
                if (string.IsNullOrWhiteSpace(room.PairKey) || activePairKeys.Contains(room.PairKey))
                {
                    continue;
                }

                room.IsActive = false;
                await _rooms.UpsertAsync(room).ConfigureAwait(false);
                _logger.LogInformation("Archived pair room {Room}", LogSanitizer.Sanitize(room.Name));
            }
        }
    }
}
