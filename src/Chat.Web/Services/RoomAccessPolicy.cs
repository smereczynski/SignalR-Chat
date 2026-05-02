using System;
using System.Collections.Generic;
using System.Linq;
using Chat.Web.Models;

namespace Chat.Web.Services
{
    public static class RoomAccessPolicy
    {
        public static string GetPrimaryDispatchCenterId(ApplicationUser user)
        {
            return string.IsNullOrWhiteSpace(user?.DispatchCenterId) ? null : user.DispatchCenterId;
        }

        public static bool IsDispatchCenterScoped(Room room)
        {
            return room != null &&
                   room.RoomType == RoomType.DispatchCenterPair &&
                   !string.IsNullOrWhiteSpace(room.PairKey) &&
                   !string.IsNullOrWhiteSpace(room.DispatchCenterAId) &&
                   !string.IsNullOrWhiteSpace(room.DispatchCenterBId);
        }

        public static bool CanAccessRoom(ApplicationUser user, Room room)
        {
            if (user == null || room == null || !room.IsActive || !user.Enabled)
            {
                return false;
            }

            var dispatchCenterId = GetPrimaryDispatchCenterId(user);
            return !string.IsNullOrWhiteSpace(dispatchCenterId) &&
                   IsDispatchCenterScoped(room) &&
                   DispatchCenterPairing.IncludesDispatchCenter(room, dispatchCenterId);
        }

        public static IEnumerable<Room> GetAccessibleRooms(ApplicationUser user, IEnumerable<Room> rooms)
        {
            return (rooms ?? Enumerable.Empty<Room>())
                .Where(room => CanAccessRoom(user, room));
        }

        public static IEnumerable<ApplicationUser> GetAssignedUsersForRoom(Room room, IEnumerable<ApplicationUser> users)
        {
            var candidates = (users ?? Enumerable.Empty<ApplicationUser>())
                .Where(user => user != null && user.Enabled);

            if (room == null)
            {
                return Enumerable.Empty<ApplicationUser>();
            }

            return candidates.Where(user =>
                !string.IsNullOrWhiteSpace(user.DispatchCenterId) &&
                IsDispatchCenterScoped(room) &&
                DispatchCenterPairing.IncludesDispatchCenter(room, user.DispatchCenterId));
        }

        public static string ResolveDispatchCenterIdForRoom(ApplicationUser user, Room room)
        {
            if (user == null)
            {
                return null;
            }

            var dispatchCenterId = GetPrimaryDispatchCenterId(user);
            return DispatchCenterPairing.IncludesDispatchCenter(room, dispatchCenterId)
                ? dispatchCenterId
                : null;
        }
    }
}
