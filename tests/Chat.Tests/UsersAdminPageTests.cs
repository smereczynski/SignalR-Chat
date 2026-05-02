using System.Threading.Tasks;
using Chat.Web.Models;
using Chat.Web.Pages.Admin.Users;
using Chat.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace Chat.Tests;

public class UsersAdminPageTests
{
    [Fact]
    public async Task EditPage_OnGet_LoadsExistingUserFields()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new ApplicationUser
        {
            UserName = "alice",
            FullName = "Alice Smith",
            Email = "alice@example.com",
            MobileNumber = "+48123456789",
            Upn = "alice@company.onmicrosoft.com",
            PreferredLanguage = "pl",
            Enabled = true,
            DispatchCenterId = "dc-1"
        });

        var page = new UsersEditModel(users) { UserName = "alice" };

        var result = await page.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal("Alice Smith", page.Input.FullName);
        Assert.Equal("alice@example.com", page.Input.Email);
        Assert.Equal("+48123456789", page.Input.MobileNumber);
        Assert.Equal("alice@company.onmicrosoft.com", page.Input.Upn);
        Assert.Equal("pl", page.Input.PreferredLanguage);
        Assert.True(page.Input.Enabled);
    }

    [Fact]
    public async Task EditPage_OnGet_MissingUser_Redirects()
    {
        var users = new InMemoryUsersRepository();
        var page = new UsersEditModel(users) { UserName = "nonexistent" };

        var result = await page.OnGetAsync();

        Assert.IsType<RedirectToPageResult>(result);
    }

    [Fact]
    public async Task EditPage_OnPost_SavesFieldsWithoutChangingDispatchCenter()
    {
        var users = new InMemoryUsersRepository();
        await users.UpsertAsync(new ApplicationUser
        {
            UserName = "bob",
            DispatchCenterId = "dc-preserved",
            Enabled = true
        });

        var page = new UsersEditModel(users)
        {
            UserName = "bob",
            Input = new UsersEditModel.InputModel
            {
                FullName = "Bob Updated",
                Email = "bob@example.com",
                MobileNumber = "+48987654321",
                Upn = "",
                PreferredLanguage = "en",
                Enabled = false
            }
        };
        page.ModelState.Clear();

        var result = await page.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);

        var saved = await users.GetByUserNameAsync("bob");
        Assert.Equal("Bob Updated", saved.FullName);
        Assert.Equal("bob@example.com", saved.Email);
        Assert.Equal("+48987654321", saved.MobileNumber);
        Assert.Equal("en", saved.PreferredLanguage);
        Assert.False(saved.Enabled);
        // DispatchCenterId must NOT be touched
        Assert.Equal("dc-preserved", saved.DispatchCenterId);
    }

    [Fact]
    public async Task EditPage_OnPost_MissingUser_Redirects()
    {
        var users = new InMemoryUsersRepository();
        var page = new UsersEditModel(users)
        {
            UserName = "ghost",
            Input = new UsersEditModel.InputModel { FullName = "Ghost" }
        };
        page.ModelState.Clear();

        var result = await page.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
    }
}
