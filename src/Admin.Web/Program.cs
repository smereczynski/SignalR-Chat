using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Auth: Entra ID only (no in-app auth)
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy; // require auth by default
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
}).AddMicrosoftIdentityUI();

// Options for Cosmos
builder.Services.Configure<CosmosOptions>(builder.Configuration.GetSection("Cosmos"));
builder.Services.AddSingleton<CosmosClients>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var opts = cfg.GetSection("Cosmos").Get<CosmosOptions>() ?? new CosmosOptions();
    return new CosmosClients(opts);
});

// Repositories
builder.Services.AddSingleton<IUsersRepository, CosmosUsersRepository>();
builder.Services.AddSingleton<IRoomsRepository, CosmosRoomsRepository>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();

// Minimal option models and repos (reused simplified versions)
public record CosmosOptions
{
    public string? ConnectionString { get; init; }
    public string Database { get; init; } = "chat";
    public string UsersContainer { get; init; } = "users";
    public string RoomsContainer { get; init; } = "rooms";
}

public class CosmosClients
{
    public Microsoft.Azure.Cosmos.CosmosClient Client { get; }
    public Microsoft.Azure.Cosmos.Database Database { get; }
    public Microsoft.Azure.Cosmos.Container Users { get; }
    public Microsoft.Azure.Cosmos.Container Rooms { get; }
    public CosmosClients(CosmosOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new InvalidOperationException("Cosmos:ConnectionString is required");
        Client = new Microsoft.Azure.Cosmos.CosmosClient(options.ConnectionString);
        Database = Client.GetDatabase(options.Database);
        Users = Database.GetContainer(options.UsersContainer);
        Rooms = Database.GetContainer(options.RoomsContainer);
    }
}

public record AdminUser
{
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string MobileNumber { get; init; } = default!;
    public bool IsAdmin { get; init; }
    public bool Enabled { get; init; } = true;
    public List<string> Rooms { get; init; } = new();
}

public interface IUsersRepository
{
    Task<IEnumerable<AdminUser>> GetAllAsync();
    Task<AdminUser?> GetAsync(string userName);
    Task UpsertAsync(AdminUser user);
}

public interface IRoomsRepository
{
    Task<IEnumerable<string>> GetAllNamesAsync();
    Task CreateAsync(string name);
}

public class CosmosUsersRepository : IUsersRepository
{
    private readonly Microsoft.Azure.Cosmos.Container _users;
    public CosmosUsersRepository(CosmosClients clients) => _users = clients.Users;
    private class UserDoc
    {
        public string id { get; set; } = default!; // userName
        public string userName { get; set; } = default!;
        public string email { get; set; } = default!;
        public string mobile { get; set; } = default!;
        public bool isAdmin { get; set; }
        public bool enabled { get; set; } = true;
        public string[] rooms { get; set; } = Array.Empty<string>();
    }
    public async Task<IEnumerable<AdminUser>> GetAllAsync()
    {
        var q = _users.GetItemQueryIterator<UserDoc>(new Microsoft.Azure.Cosmos.QueryDefinition("SELECT * FROM c"));
        var list = new List<AdminUser>();
        while (q.HasMoreResults)
        {
            var page = await q.ReadNextAsync();
            list.AddRange(page.Select(d => new AdminUser { UserName = d.userName, Email = d.email, MobileNumber = d.mobile, IsAdmin = d.isAdmin, Enabled = d.enabled, Rooms = d.rooms?.ToList() ?? new List<string>() }));
        }
        return list;
    }
    public async Task<AdminUser?> GetAsync(string userName)
    {
        var q = _users.GetItemQueryIterator<UserDoc>(new Microsoft.Azure.Cosmos.QueryDefinition("SELECT * FROM c WHERE c.userName = @u").WithParameter("@u", userName));
        while (q.HasMoreResults)
        {
            var page = await q.ReadNextAsync();
            var d = page.FirstOrDefault();
            if (d != null) return new AdminUser { UserName = d.userName, Email = d.email, MobileNumber = d.mobile, IsAdmin = d.isAdmin, Enabled = d.enabled, Rooms = d.rooms?.ToList() ?? new List<string>() };
        }
        return null;
    }
    public async Task UpsertAsync(AdminUser user)
    {
        var doc = new UserDoc { id = user.UserName, userName = user.UserName, email = user.Email, mobile = user.MobileNumber, isAdmin = user.IsAdmin, enabled = user.Enabled, rooms = user.Rooms?.ToArray() ?? Array.Empty<string>() };
        await _users.UpsertItemAsync(doc, new Microsoft.Azure.Cosmos.PartitionKey(doc.userName));
    }
}

public class CosmosRoomsRepository : IRoomsRepository
{
    private readonly Microsoft.Azure.Cosmos.Container _rooms;
    public CosmosRoomsRepository(CosmosClients clients) => _rooms = clients.Rooms;
    private class RoomDoc { public string id { get; set; } = default!; public string name { get; set; } = default!; }
    public async Task<IEnumerable<string>> GetAllNamesAsync()
    {
        var q = _rooms.GetItemQueryIterator<RoomDoc>(new Microsoft.Azure.Cosmos.QueryDefinition("SELECT c.name FROM c"));
        var list = new List<string>();
        while (q.HasMoreResults)
        {
            var page = await q.ReadNextAsync();
            list.AddRange(page.Select(d => d.name));
        }
        return list.OrderBy(n => n);
    }
    public async Task CreateAsync(string name)
    {
        var doc = new RoomDoc { id = Guid.NewGuid().ToString("N"), name = name };
        await _rooms.CreateItemAsync(doc, new Microsoft.Azure.Cosmos.PartitionKey(name));
    }
}
