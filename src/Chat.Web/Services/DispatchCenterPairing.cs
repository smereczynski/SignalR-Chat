using System;
using Chat.Web.Models;

namespace Chat.Web.Services
{
    public static class DispatchCenterPairing
    {
        public static string BuildPairKey(string leftDispatchCenterId, string rightDispatchCenterId)
        {
            if (string.IsNullOrWhiteSpace(leftDispatchCenterId)) throw new ArgumentException("Value is required.", nameof(leftDispatchCenterId));
            if (string.IsNullOrWhiteSpace(rightDispatchCenterId)) throw new ArgumentException("Value is required.", nameof(rightDispatchCenterId));

            var compare = string.Compare(leftDispatchCenterId, rightDispatchCenterId, StringComparison.OrdinalIgnoreCase);
            return compare <= 0
                ? $"{leftDispatchCenterId}::{rightDispatchCenterId}"
                : $"{rightDispatchCenterId}::{leftDispatchCenterId}";
        }

        public static string BuildRoomName(string pairKey) => $"pair:{pairKey}";

        public static string BuildRoomDisplayName(DispatchCenter left, DispatchCenter right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            return $"{left.Name} <-> {right.Name}";
        }

        public static bool IncludesDispatchCenter(Room room, string dispatchCenterId)
        {
            if (room == null || string.IsNullOrWhiteSpace(dispatchCenterId))
            {
                return false;
            }

            return string.Equals(room.DispatchCenterAId, dispatchCenterId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(room.DispatchCenterBId, dispatchCenterId, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetCounterpartDispatchCenterId(Room room, string dispatchCenterId)
        {
            if (!IncludesDispatchCenter(room, dispatchCenterId))
            {
                return null;
            }

            return string.Equals(room.DispatchCenterAId, dispatchCenterId, StringComparison.OrdinalIgnoreCase)
                ? room.DispatchCenterBId
                : room.DispatchCenterAId;
        }
    }
}
