using System.Collections.Generic;
using System.Linq;
using Chat.Web.Models;
using Chat.Web.Services;
using Xunit;

namespace Chat.Tests
{
    public class RoomAccessPolicyTests
    {
        [Fact]
        public void CanAccessRoom_AllowsActivePairRoomForAssignedDispatchCenter()
        {
            var user = new ApplicationUser
            {
                UserName = "michal.s@free-media.eu",
                Enabled = true,
                DispatchCenterId = "dc-alpha"
            };

            var room = new Room
            {
                Name = "pair:dc-alpha::dc-beta",
                IsActive = true,
                RoomType = RoomType.DispatchCenterPair,
                PairKey = "dc-alpha::dc-beta",
                DispatchCenterAId = "dc-alpha",
                DispatchCenterBId = "dc-beta"
            };

            Assert.True(RoomAccessPolicy.CanAccessRoom(user, room));
        }

        [Fact]
        public void CanAccessRoom_DeniesRoomOutsideUsersDispatchCenter()
        {
            var user = new ApplicationUser
            {
                UserName = "michal.s@free-media.eu",
                Enabled = true,
                DispatchCenterId = "dc-alpha"
            };

            var room = new Room
            {
                Name = "pair:dc-beta::dc-gamma",
                IsActive = true,
                RoomType = RoomType.DispatchCenterPair,
                PairKey = "dc-beta::dc-gamma",
                DispatchCenterAId = "dc-beta",
                DispatchCenterBId = "dc-gamma"
            };

            Assert.False(RoomAccessPolicy.CanAccessRoom(user, room));
            Assert.Null(RoomAccessPolicy.ResolveDispatchCenterIdForRoom(user, room));
        }

        [Fact]
        public void GetAssignedUsersForRoom_ReturnsUsersFromBothSidesOfPair()
        {
            var room = new Room
            {
                Name = "pair:dc-alpha::dc-beta",
                IsActive = true,
                RoomType = RoomType.DispatchCenterPair,
                PairKey = "dc-alpha::dc-beta",
                DispatchCenterAId = "dc-alpha",
                DispatchCenterBId = "dc-beta"
            };

            var users = new[]
            {
                new ApplicationUser { UserName = "michal.s@free-media.eu", Enabled = true, DispatchCenterId = "dc-alpha" },
                new ApplicationUser { UserName = "jan.kowalski@free-media.eu", Enabled = true, DispatchCenterId = "dc-beta" },
                new ApplicationUser { UserName = "outsider@free-media.eu", Enabled = true, DispatchCenterId = "dc-gamma" }
            };

            var visible = RoomAccessPolicy.GetAssignedUsersForRoom(room, users).Select(x => x.UserName).OrderBy(x => x).ToArray();

            Assert.Equal(
                new[] { "jan.kowalski@free-media.eu", "michal.s@free-media.eu" },
                visible);
        }
    }
}
