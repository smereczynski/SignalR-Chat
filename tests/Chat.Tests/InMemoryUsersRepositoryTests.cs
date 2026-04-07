using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Repositories;
using Xunit;

namespace Chat.Tests;

public class InMemoryUsersRepositoryTests
{
    [Fact]
    public async Task GetByUpnAsync_IsCaseInsensitive()
    {
        var repo = new InMemoryUsersRepository();
        await repo.UpsertAsync(new ApplicationUser
        {
            UserName = "michal.s@free-media.eu",
            Upn = "michal.s@free-media.eu",
            DispatchCenterId = "dc-a"
        });

        var user = await repo.GetByUpnAsync("MICHAL.S@FREE-MEDIA.EU");

        Assert.NotNull(user);
        Assert.Equal("michal.s@free-media.eu", user.UserName);
    }
}
