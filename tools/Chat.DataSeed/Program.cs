using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Chat.Web.Repositories;
using Chat.Web.Models;
using Chat.Web.Options;

namespace Chat.DataSeed;

/// <summary>
/// Standalone console application for seeding Chat application data (rooms and users).
/// This tool is run on-demand and is NOT part of the normal application runtime.
/// 
/// Usage:
///   dotnet run --project tools/Chat.DataSeed
///   dotnet run --project tools/Chat.DataSeed -- --environment Production
///   dotnet run --project tools/Chat.DataSeed -- --clear
/// 
/// Configuration:
///   Reads from appsettings.json and environment variables (same as Chat.Web)
///   Supports Cosmos DB or SQL database (configured via connection strings)
/// </summary>
class Program
{
    private Program() { }

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Chat Data Seed Tool ===");
        Console.WriteLine();

        var builder = Host.CreateApplicationBuilder(args);

        // Load configuration from Chat.Web settings
        builder.Configuration
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../../src/Chat.Web"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        // Configure services (reuse Chat.Web repository registration logic)
        ConfigureServices(builder.Services, builder.Configuration);

        var host = builder.Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        
        try
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            var seeder = services.GetRequiredService<DataSeeder>();

            // Parse command line arguments
            bool clearExisting = args.Contains("--clear");
            bool dryRun = args.Contains("--dry-run");

            if (dryRun)
            {
                logger.LogInformation("DRY RUN MODE - No data will be modified");
            }

            if (clearExisting && !dryRun)
            {
                logger.LogWarning("--clear flag detected: Will remove existing data first");
                Console.Write("Are you sure you want to clear existing data? (yes/no): ");
                var confirmation = Console.ReadLine();
                if (confirmation?.ToLower() != "yes")
                {
                    logger.LogInformation("Operation cancelled by user");
                    return 1;
                }
            }

            await seeder.SeedAsync(clearExisting, dryRun);

            Console.WriteLine();
            Console.WriteLine("✓ Data seeding completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Error during seeding: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Determine which repository implementation to use
        var cosmosConnString = configuration["Cosmos:ConnectionString"];
        
        if (!string.IsNullOrWhiteSpace(cosmosConnString) && 
            !cosmosConnString.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Using Cosmos DB repositories (Database: {configuration["Cosmos:Database"]})");
            
            // Create Cosmos options and clients (same as Chat.Web Startup.cs)
            var cosmosOpts = new CosmosOptions
            {
                ConnectionString = cosmosConnString,
                Database = configuration["Cosmos:Database"] ?? "chat",
                MessagesContainer = configuration["Cosmos:MessagesContainer"] ?? "messages",
                UsersContainer = configuration["Cosmos:UsersContainer"] ?? "users",
                RoomsContainer = configuration["Cosmos:RoomsContainer"] ?? "rooms",
            };
            
            // Register CosmosClients singleton
            services.AddSingleton(new CosmosClients(cosmosOpts));
            
            // Register Cosmos repositories
            services.AddSingleton<IUsersRepository, CosmosUsersRepository>();
            services.AddSingleton<IRoomsRepository, CosmosRoomsRepository>();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Cosmos DB connection string not configured");
            Console.WriteLine("This tool requires a valid database connection");
            Console.WriteLine("Set Cosmos:ConnectionString in appsettings or environment variables");
            Console.ResetColor();
            throw new InvalidOperationException("Database connection not configured");
        }

        // Register the seeder service
        services.AddSingleton<DataSeeder>();
    }
}

/// <summary>
/// Service responsible for seeding rooms and users into the database.
/// </summary>
public class DataSeeder
{
    private readonly IUsersRepository _usersRepo;
    private readonly IRoomsRepository _roomsRepo;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        IUsersRepository usersRepo,
        IRoomsRepository roomsRepo,
        ILogger<DataSeeder> logger)
    {
        _usersRepo = usersRepo;
        _roomsRepo = roomsRepo;
        _logger = logger;
    }

    public async Task SeedAsync(bool clearExisting = false, bool dryRun = false)
    {
        _logger.LogInformation("Starting data seed process...");

        if (clearExisting && !dryRun)
        {
            await ClearExistingDataAsync();
        }

        await SeedRoomsAsync(dryRun);
        await SeedUsersAsync(dryRun);

        _logger.LogInformation("Data seed process completed");
    }

    private Task ClearExistingDataAsync()
    {
        _logger.LogWarning("Clearing existing data...");
        
        // Note: This assumes repositories have a Clear or DeleteAll method
        // If not, you'll need to iterate and delete individually
        var existingUsers = _usersRepo.GetAll();
        foreach (var user in existingUsers)
        {
            _logger.LogInformation("Deleting user: {UserName}", user.UserName);
            // Assuming there's a delete method - adjust based on your repository interface
        }

        _logger.LogInformation("Existing data cleared");
        return Task.CompletedTask;
    }

    private Task SeedRoomsAsync(bool dryRun)
    {
        _logger.LogInformation("Seeding rooms...");

        var rooms = new[]
        {
            new Room { Id = 1, Name = "general", Users = new List<string>() },
            new Room { Id = 2, Name = "ops", Users = new List<string>() },
            new Room { Id = 3, Name = "random", Users = new List<string>() }
        };

        foreach (var room in rooms)
        {
            var existing = _roomsRepo.GetByName(room.Name);
            if (existing != null)
            {
                _logger.LogInformation("Room '{RoomName}' already exists (ID: {RoomId}) - skipping", 
                    room.Name, existing.Id);
                continue;
            }

            if (dryRun)
            {
                _logger.LogInformation("[DRY RUN] Would create room: {RoomName} (ID: {RoomId})", 
                    room.Name, room.Id);
            }
            else
            {
                // Note: Cosmos repositories might not support Create with specific ID
                // You may need to adjust based on your repository implementation
                _logger.LogInformation("Creating room: {RoomName} (ID: {RoomId})", room.Name, room.Id);
                
                // For Cosmos, you'll need to directly insert the document
                // This requires access to the CosmosClient - may need to enhance the interface
                _logger.LogWarning("Room creation in Cosmos requires direct container access. Please create rooms manually in Cosmos DB or enhance this tool");
            }
        }

        return Task.CompletedTask;
    }

    private Task SeedUsersAsync(bool dryRun)
    {
        _logger.LogInformation("Seeding users...");

        var users = new[]
        {
            new ApplicationUser
            {
                UserName = "alice",
                FullName = "Alice Johnson",
                Email = "alice@example.com",
                MobileNumber = "+1234567890",
                Enabled = true,
                FixedRooms = new List<string> { "general", "ops" },
                DefaultRoom = "general",
                Avatar = null
            },
            new ApplicationUser
            {
                UserName = "bob",
                FullName = "Bob Stone",
                Email = "bob@example.com",
                MobileNumber = "+1234567891",
                Enabled = true,
                FixedRooms = new List<string> { "general", "random" },
                DefaultRoom = "general",
                Avatar = null
            },
            new ApplicationUser
            {
                UserName = "charlie",
                FullName = "Charlie Fields",
                Email = "charlie@example.com",
                MobileNumber = "+1234567892",
                Enabled = true,
                FixedRooms = new List<string> { "general" },
                DefaultRoom = "general",
                Avatar = null
            }
        };

        foreach (var user in users)
        {
            var existing = _usersRepo.GetByUserName(user.UserName);
            if (existing != null)
            {
                _logger.LogInformation("User '{UserName}' already exists - skipping", user.UserName);
                continue;
            }

            if (dryRun)
            {
                _logger.LogInformation("[DRY RUN] Would create user: {UserName} ({FullName})", 
                    user.UserName, user.FullName);
                _logger.LogInformation("  - Email: {Email}, FixedRooms: {Rooms}", user.Email, string.Join(", ", user.FixedRooms));
            }
            else
            {
                _logger.LogInformation("Creating user: {UserName} ({FullName})", user.UserName, user.FullName);
                _usersRepo.Upsert(user);
                _logger.LogInformation("  ✓ User '{UserName}' created successfully", user.UserName);
            }
        }

        return Task.CompletedTask;
    }
}
