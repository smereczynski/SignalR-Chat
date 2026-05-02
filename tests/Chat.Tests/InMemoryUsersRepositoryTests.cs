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

    [Fact]
    public async Task GetByUserNameAsync_IsCaseInsensitive()
    {
        var repo = new InMemoryUsersRepository();
        await repo.UpsertAsync(new ApplicationUser
        {
            UserName = "alice",
            DispatchCenterId = "dc-a"
        });

        var user = await repo.GetByUserNameAsync("ALICE");

        Assert.NotNull(user);
        Assert.Equal("alice", user.UserName);
    }

    [Fact]
    public async Task UpsertAsync_PreservesPreferredLanguage_WhenCallerDoesNotProvideIt()
    {
        var repo = new InMemoryUsersRepository();
        await repo.UpsertAsync(new ApplicationUser
        {
            UserName = "alice",
            PreferredLanguage = "pl",
            FullName = "Alice"
        });

        await repo.UpsertAsync(new ApplicationUser
        {
            UserName = "alice",
            PreferredLanguage = null,
            FullName = "Alice Updated"
        });

        var user = await repo.GetByUserNameAsync("alice");

        Assert.NotNull(user);
        Assert.Equal("pl", user.PreferredLanguage);
        Assert.Equal("Alice Updated", user.FullName);
    }
}
