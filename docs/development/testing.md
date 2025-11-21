# Testing Guide

This guide covers testing practices, running tests, and understanding test failures in SignalR Chat.

## Test Structure

SignalR Chat has **179 total tests** across three projects:

| Project | Count | Purpose | Speed |
|---------|-------|---------|-------|
| **Chat.Tests** | 9 | Unit tests (utilities, helpers) | ⚡ Fast |
| **Chat.IntegrationTests** | 135 | Domain tests (auth, SignalR, API) | 🐢 Slow |
| **Chat.Web.Tests** | 35 | Web tests (health, security headers) | ⚡ Fast |

## Running Tests

### All Tests

```bash
# Run all 179 tests
dotnet test src/Chat.sln

# With detailed output
dotnet test src/Chat.sln --logger "console;verbosity=detailed"

# With coverage (if configured)
dotnet test src/Chat.sln /p:CollectCoverage=true
```

### Specific Test Projects

```bash
# Unit tests only (9 tests) - utilities, helpers
dotnet test tests/Chat.Tests/

# Integration tests (135 tests) - SignalR, auth, API
dotnet test tests/Chat.IntegrationTests/

# Web tests (35 tests) - health checks, security
dotnet test tests/Chat.Web.Tests/
```

### Specific Test Classes

```bash
# Run specific test class
dotnet test --filter "FullyQualifiedName~OtpAuthFlowTests"

# Run specific test method
dotnet test --filter "FullyQualifiedName~OtpAuthFlowTests.ValidOtp_ShouldAuthenticateUser"
```

### Test Categories

```bash
# Run by category (if configured)
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
```

### VS Code Tasks

Use pre-configured tasks:
- **Test** (`Cmd+Shift+P` → "Run Task" → "test")
- Runs: `dotnet test src/Chat.sln --no-build --nologo`

## Test Modes

### 1. In-Memory Mode (Default)

```bash
dotnet test src/Chat.sln
```

**What it uses**:
- ✅ In-memory OTP storage (no Redis)
- ✅ In-memory database (no Cosmos DB)
- ✅ Local SignalR connections (no Azure SignalR Service)

**Test results**:
- ✅ **168/179 tests pass** (94%)
- ❌ **11-14 SignalR tests fail** (expected - see [Known Issues](#known-issues))

**When to use**: 
- Local development
- CI/CD pipelines without Azure
- Fast feedback loop

### 2. Azure Mode (Full Integration)

```bash
# Load .env.local and run tests
bash -lc "set -a; source .env.local; dotnet test src/Chat.sln"
```

**What it uses**:
- ✅ Azure Cache for Redis (OTP storage)
- ✅ Azure Cosmos DB (persistent database)
- ✅ Azure SignalR Service (load-balanced connections)

**Test results**:
- ✅ **179/179 tests pass** (100%)
- ✅ All SignalR tests work with Azure

**When to use**:
- Testing Azure integration
- Validating production-like scenarios
- Debugging SignalR connection issues

## Test Projects Overview

### Chat.Tests (Unit Tests)

**Location**: `tests/Chat.Tests/`  
**Count**: 9 tests  
**Focus**: Pure logic, no dependencies

**Test files**:
```
Chat.Tests/
├── ConfigurationGuardsTests.cs          # Configuration validation
├── LocalizationTests.cs                 # i18n resource loading
├── LogSanitizerTests.cs                 # Log injection prevention
├── OtpHasherTests.cs                    # Argon2id hashing
├── UnreadNotificationSchedulerTests.cs  # Background job logic
└── UrlIsLocalUrlTests.cs                # URL validation
```

**Example test**:
```csharp
[Fact]
public void Sanitize_WithNewlines_RemovesNewlines()
{
    // Arrange
    var input = "Line1\nLine2\rLine3\r\nLine4";
    
    // Act
    var result = LogSanitizer.Sanitize(input);
    
    // Assert
    Assert.Equal("Line1Line2Line3Line4", result);
}
```

**Run unit tests only**:
```bash
dotnet test tests/Chat.Tests/
# Output: 9/9 passed (< 1 second)
```

### Chat.IntegrationTests (Domain Tests)

**Location**: `tests/Chat.IntegrationTests/`  
**Count**: 135 tests  
**Focus**: API endpoints, SignalR hubs, authentication flows

**Test files**:
```
Chat.IntegrationTests/
├── ChatHubLifecycleTests.cs         # SignalR connection lifecycle
├── CustomWebApplicationFactory.cs   # Test server setup
├── ImmediatePostAfterLoginTests.cs  # Auth session validation
├── MarkReadRateLimitingTests.cs     # Rate limiting for read receipts
├── OtpAttemptLimitingTests.cs       # OTP brute-force protection
├── OtpAuthFlowTests.cs              # Full OTP authentication flow
├── RateLimitingTests.cs             # Message send rate limiting
├── RoomAuthorizationTests.cs        # Room access control
├── RoomJoinPositiveTests.cs         # SignalR room joining (35 tests)
└── RoomsAuthorizationTests.cs       # Rooms API authorization
```

**Example test**:
```csharp
[Fact]
public async Task SendMessage_ValidInput_ReturnsSuccess()
{
    // Arrange
    var connection = await CreateAuthenticatedConnectionAsync("alice");
    await connection.InvokeAsync("JoinRoom", "general");
    
    // Act
    await connection.InvokeAsync("SendMessage", "general", "Hello");
    
    // Assert
    var messages = await _client.GetFromJsonAsync<List<Message>>("/api/rooms/general/messages");
    Assert.Contains(messages, m => m.Content == "Hello");
}
```

**Run integration tests only**:
```bash
dotnet test tests/Chat.IntegrationTests/
# Output: 124-135/135 passed (10-30 seconds)
```

**⚠️ Note**: 11-14 tests may fail without Azure SignalR Service. See [Known Issues](#known-issues).

### Chat.Web.Tests (Web/Security Tests)

**Location**: `tests/Chat.Web.Tests/`  
**Count**: 35 tests  
**Focus**: HTTP endpoints, security headers, health checks

**Test files**:
```
Chat.Web.Tests/
├── HealthEndpointsTests.cs      # /health, /healthz endpoints
└── SecurityHeadersTests.cs      # CSP, HSTS, X-Frame-Options
```

**Example test**:
```csharp
[Fact]
public async Task HealthEndpoint_ReturnsHealthyStatus()
{
    // Arrange
    var client = _factory.CreateClient();
    
    // Act
    var response = await client.GetAsync("/health");
    
    // Assert
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync();
    Assert.Contains("Healthy", content);
}
```

**Run web tests only**:
```bash
dotnet test tests/Chat.Web.Tests/
# Output: 35/35 passed (< 5 seconds)
```

## Known Issues

### SignalR Integration Tests Fail Locally

**Issue**: 11-14 tests in `RoomJoinPositiveTests.cs` fail with **401 Unauthorized** when running without Azure SignalR Service.

**Example failure**:
```
Microsoft.AspNetCore.SignalR.Client.HubException: 
Failed to complete negotiation with the server: Unauthorized
```

**Root Cause**:
- `TestAuthHandler` (custom authentication for tests) is **not invoked** for SignalR WebSocket/SSE connections
- SignalR bypasses middleware authentication when using local transport
- Tests pass with Azure SignalR Service (uses query string auth)

**Why It Happens**:
```
Local SignalR Flow:
1. Client → /chathub/negotiate (HTTP) → TestAuthHandler ✅ works
2. Client → /chathub (WebSocket/SSE) → TestAuthHandler ❌ not invoked

Azure SignalR Flow:
1. Client → /chathub/negotiate (HTTP) → Returns Azure SignalR URL
2. Client → Azure SignalR Service → Query string auth ✅ works
```

**Impact**:
- ❌ 11-14 tests fail locally without Azure
- ✅ Tests pass with `.env.local` (Azure SignalR configured)
- ✅ Tests pass in GitHub Actions (uses Azure resources)

**Workarounds**:

**Option 1**: Run tests with Azure SignalR Service (recommended)
```bash
# Load .env.local with Azure connection strings
bash -lc "set -a; source .env.local; dotnet test src/Chat.sln"
# Output: 179/179 passed ✅
```

**Option 2**: Run tests without SignalR failures
```bash
# Skip SignalR integration tests
dotnet test --filter "FullyQualifiedName!~RoomJoinPositiveTests"
# Output: 144/144 passed ✅
```

**Option 3**: Accept failures as expected
```bash
dotnet test src/Chat.sln
# Output: 168/179 passed (11 expected failures) ⚠️
```

**Why Not Fixed?**:
- Creating custom SignalR authentication for tests is complex
- Tests work perfectly with Azure resources (production scenario)
- In-memory mode is for development, not SignalR testing
- Not worth the engineering effort for low-value fix

**References**:
- [Issue #113: Fix failing SignalR hub integration tests](https://github.com/smereczynski/SignalR-Chat/issues/113)
- [FAQ: Why do SignalR tests fail locally?](../reference/faq.md#why-do-signalr-tests-fail-locally)

## Writing Tests

### Unit Test Template

```csharp
using Xunit;

namespace Chat.Tests
{
    public class MyServiceTests
    {
        [Fact]
        public void MethodName_Scenario_ExpectedBehavior()
        {
            // Arrange
            var service = new MyService();
            var input = "test";
            
            // Act
            var result = service.DoSomething(input);
            
            // Assert
            Assert.Equal("expected", result);
        }
    }
}
```

### Integration Test Template

```csharp
using Xunit;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests
{
    public class MyHubTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        private readonly HttpClient _client;
        
        public MyHubTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }
        
        [Fact]
        public async Task HubMethod_ValidInput_ReturnsSuccess()
        {
            // Arrange
            var connection = await CreateAuthenticatedConnectionAsync("alice");
            
            // Act
            await connection.InvokeAsync("MyMethod", "param");
            
            // Assert
            // Verify expected behavior
        }
        
        private async Task<HubConnection> CreateAuthenticatedConnectionAsync(string userId)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl($"http://localhost/chathub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.Headers.Add("X-Test-UserId", userId);
                })
                .Build();
            
            await connection.StartAsync();
            return connection;
        }
    }
}
```

### Testing Best Practices

✅ **Do**:
- Use descriptive test names: `MethodName_Scenario_ExpectedBehavior`
- Follow AAA pattern: Arrange, Act, Assert
- Test one thing per test
- Use `[Theory]` with `[InlineData]` for parameterized tests
- Mock external dependencies (databases, APIs)
- Clean up resources in `Dispose()` or `IAsyncDisposable.DisposeAsync()`

❌ **Don't**:
- Test implementation details (test behavior, not internals)
- Use `Thread.Sleep()` (use `TaskCompletionSource` or `await`)
- Share state between tests (tests must be independent)
- Ignore intermittent failures (fix flaky tests)
- Skip tests without explanation (document with `[Fact(Skip = "reason")]`)

### Parameterized Tests

```csharp
[Theory]
[InlineData("alice", true)]
[InlineData("bob", true)]
[InlineData("invalid", false)]
public async Task Login_WithUsername_ReturnsExpectedResult(string username, bool shouldSucceed)
{
    // Arrange
    var client = _factory.CreateClient();
    
    // Act
    var response = await client.PostAsync($"/login?username={username}", null);
    
    // Assert
    if (shouldSucceed)
    {
        Assert.True(response.IsSuccessStatusCode);
    }
    else
    {
        Assert.False(response.IsSuccessStatusCode);
    }
}
```

### Async Tests

```csharp
[Fact]
public async Task AsyncMethod_ReturnsExpectedValue()
{
    // Arrange
    var service = new MyService();
    
    // Act
    var result = await service.GetDataAsync();
    
    // Assert
    Assert.NotNull(result);
}
```

### Testing Exceptions

```csharp
[Fact]
public void Method_WithInvalidInput_ThrowsException()
{
    // Arrange
    var service = new MyService();
    
    // Act & Assert
    Assert.Throws<ArgumentException>(() => service.Process(null));
}

[Fact]
public async Task AsyncMethod_WithInvalidInput_ThrowsException()
{
    // Arrange
    var service = new MyService();
    
    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.ProcessAsync(null)
    );
}
```

## CustomWebApplicationFactory

Integration tests use `CustomWebApplicationFactory` to:
- Create test server with in-memory database
- Configure test authentication (`TestAuthHandler`)
- Override services for testing
- Provide `HttpClient` for API testing

**Key features**:
```csharp
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Use in-memory database
            services.Configure<TestingOptions>(o => o.InMemory = true);
            
            // Add test authentication
            services.AddAuthentication("Test")
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>("Test", null);
        });
    }
}
```

**Usage in tests**:
```csharp
public class MyTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    
    public MyTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Test()
    {
        var response = await _client.GetAsync("/api/endpoint");
        response.EnsureSuccessStatusCode();
    }
}
```

## CI/CD Testing

### GitHub Actions

Tests run automatically on:
- ✅ **Pull requests** - All branches
- ✅ **Push to main** - After merge
- ✅ **Scheduled** - Nightly builds

**Workflow**: `.github/workflows/ci.yml`

```yaml
- name: Run tests
  run: dotnet test src/Chat.sln --no-build --verbosity normal
  env:
    Testing__InMemory: true  # Use in-memory mode (no Azure)
```

**Test results**:
- ✅ All tests must pass before merge
- ❌ PR blocked if tests fail
- 📊 Test results visible in PR checks

### Local CI Simulation

```bash
# Run tests exactly as CI does
Testing__InMemory=true dotnet test src/Chat.sln --no-build --verbosity normal
```

## Code Coverage

### Generate Coverage Report

```bash
# Install coverage tool
dotnet tool install --global dotnet-reportgenerator-globaltool

# Run tests with coverage
dotnet test src/Chat.sln /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Generate HTML report
reportgenerator \
  -reports:"tests/**/coverage.opencover.xml" \
  -targetdir:"coverage" \
  -reporttypes:"Html"

# Open report
open coverage/index.html  # macOS
start coverage/index.html  # Windows
```

### Coverage Goals

- **Target**: >80% code coverage
- **Unit tests**: >90% coverage (pure logic)
- **Integration tests**: >70% coverage (includes I/O)
- **Focus**: Business logic, authentication, authorization

## Debugging Tests

### VS Code

1. Open test file
2. Click "Debug Test" above test method
3. Set breakpoints
4. Inspect variables in Debug Console

### Visual Studio

1. Right-click test method → **Debug Test**
2. Set breakpoints
3. Use Locals/Autos window to inspect

### Command Line

```bash
# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific test with debugging
dotnet test --filter "FullyQualifiedName~MyTest" --logger "console;verbosity=detailed"
```

### Common Debugging Scenarios

**SignalR connection issues**:
```csharp
// Add detailed logging
var connection = new HubConnectionBuilder()
    .WithUrl("...")
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddConsole();
    })
    .Build();
```

**HTTP request/response inspection**:
```csharp
// Log request details
var response = await _client.GetAsync("/api/endpoint");
var content = await response.Content.ReadAsStringAsync();
_output.WriteLine($"Status: {response.StatusCode}");
_output.WriteLine($"Content: {content}");
```

**Async/await issues**:
```csharp
// Avoid deadlocks - always await
var result = await service.GetDataAsync();  // ✅ Good
var result = service.GetDataAsync().Result; // ❌ Bad (can deadlock)
```

## Test Performance

### Slow Tests

```bash
# Find slow tests (> 1 second)
dotnet test --logger "console;verbosity=detailed" | grep -E "\d+\.\d+ sec"
```

**Common causes**:
- Database queries (use in-memory)
- HTTP requests (use `HttpClient` mock)
- `Thread.Sleep()` (use `Task.Delay` or `TaskCompletionSource`)
- Real SignalR connections (use Azure mode or skip)

### Parallel Execution

xUnit runs tests in parallel by default (one class per thread).

**Disable parallel execution** (if tests interfere):
```csharp
[Collection("Sequential")]
public class MyTests
{
    // Tests run sequentially
}
```

**Control parallelism**:
```bash
# Run tests in parallel (default)
dotnet test --parallel

# Run sequentially
dotnet test -- xUnit.ParallelizeAssembly=false
```

## Test Data

### Fixed Users

Tests use predefined users (no registration):
```csharp
public static class TestUsers
{
    public const string Alice = "alice";
    public const string Bob = "bob";
    public const string Charlie = "charlie";
    public const string Dave = "dave";
    public const string Eve = "eve";
}
```

### Fixed Rooms

Tests use predefined rooms:
```csharp
public static class TestRooms
{
    public const string General = "general";
    public const string Tech = "tech";
    public const string Random = "random";
    public const string Sports = "sports";
}
```

### Test OTP Codes

In-memory mode logs OTP codes:
```
info: Chat.Web.Services.RedisOtpStore[0]
      OTP code for alice: 123456
```

## Troubleshooting

### Issue: Tests Fail with "Cosmos:Database not configured"

**Solution**: Force in-memory mode:
```bash
Testing__InMemory=true dotnet test src/Chat.sln
```

### Issue: SignalR Tests Fail with 401 Unauthorized

**Expected behavior** - see [Known Issues](#known-issues)

**Solution**: Use Azure mode:
```bash
bash -lc "set -a; source .env.local; dotnet test src/Chat.sln"
```

### Issue: Tests Pass Locally, Fail in CI

**Possible causes**:
1. **Environment variables** - CI uses in-memory mode
2. **Timing issues** - CI is slower, may timeout
3. **Dependencies** - Missing Redis, Cosmos DB

**Solution**: Simulate CI locally:
```bash
Testing__InMemory=true dotnet test src/Chat.sln
```

### Issue: Flaky Tests (Pass/Fail Intermittently)

**Common causes**:
1. **Race conditions** - Use proper synchronization (`await`, locks)
2. **Shared state** - Tests must be independent
3. **External dependencies** - Mock or stub

**Solution**: Isolate and debug:
```bash
# Run flaky test 10 times
for i in {1..10}; do 
  dotnet test --filter "FullyQualifiedName~FlakyTest"
done
```

### Issue: Out of Memory During Tests

**Solution**: Run tests sequentially:
```bash
dotnet test -- xUnit.ParallelizeAssembly=false
```

## Next Steps

- **[Local Setup Guide](local-setup.md)** - Development environment
- **[Contributing Guide](../../CONTRIBUTING.md)** - Contribution workflow
- **[Architecture Overview](../architecture/overview.md)** - System design
- **[FAQ](../reference/faq.md)** - Common questions

## Resources

- [xUnit Documentation](https://xunit.net/)
- [ASP.NET Core Testing](https://learn.microsoft.com/aspnet/core/test/)
- [SignalR Testing](https://learn.microsoft.com/aspnet/core/signalr/testing)
- [Moq Documentation](https://github.com/moq/moq4)

---

**Happy testing!** ✅
