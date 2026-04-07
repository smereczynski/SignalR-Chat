# Testing Guide

This guide covers testing practices, running tests, and understanding the test structure in SignalR Chat.

## Test Structure

SignalR Chat has comprehensive unit and integration tests:

| Project | Count | Purpose | Speed |
|---------|-------|---------|-------|
| **Chat.Tests** | 86 | Unit tests (utilities, services, models, queue operations) | ⚡ Fast |
| **Chat.IntegrationTests** | 70 | Integration tests (ChatHub, auth flows, rate limiting, translation) | ⚡⚡ Medium |
| **Chat.Web.Tests** | 9 | Web/security tests (health endpoints, security headers) | ⚡ Fast |
| **Total** | **165** | All tests passing (100% success rate) | ⚡ Fast-Medium |

### Test Breakdown by Category

- ✅ **55 localization tests** - Multi-language resource loading and validation
- ✅ **23 translation tests** - Message translation models, queue, service integration
  - 14 TranslationModelsTests (models, serialization, status lifecycle)
  - 9 TranslationJobQueueTests (Redis queue operations, priority, FIFO)
- ✅ **14 ChatHub integration tests** - Real-time messaging, room join/leave, authorization
- ✅ **13 security tests** - OTP flows, rate limiting, room authorization
- ✅ **9 web/health tests** - Health endpoints, security headers, CSP
- ✅ **51 other tests** - Configuration, utilities, schedulers, URL validation

## Running Tests

### All Tests

```bash
# Run all tests
dotnet test src/Chat.sln

# With detailed output
dotnet test src/Chat.sln --logger "console;verbosity=detailed"

# With coverage (if configured)
dotnet test src/Chat.sln /p:CollectCoverage=true
```

### Specific Test Project

```bash
# Unit tests - utilities, services, business logic
dotnet test tests/Chat.Tests/
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
- ✅ **165/165 tests pass** (100%)

**When to use**: 
- Local development
- CI/CD pipelines without Azure
- Fast feedback loop

### 2. Azure Mode (Full Integration)

```bash
# Load .env.local and run tests with Azure resources
bash -lc "set -a; source .env.local; dotnet test src/Chat.sln"
```

**What it uses**:
- ✅ Azure Cache for Redis (OTP storage, translation queue)
- ✅ Azure Cosmos DB (persistent database)
- ✅ Azure SignalR Service (load-balanced connections)
- ✅ Azure AI Foundry (GPT-4o-mini for translation integration tests)

**Test results**:
- ✅ **165/165 tests pass** (100%)
- ✅ Full Azure integration tested

**When to use**:
- Testing Azure integration
- Validating production-like scenarios
- Testing translation service with real AI API
- Debugging distributed scenarios

## Test Projects Overview

### Chat.Tests (Unit Tests)

**Location**: `tests/Chat.Tests/`  
**Count**: 86 tests  
**Focus**: Pure logic, models, services, no Azure dependencies

**Test files**:
```
Chat.Tests/
├── ConfigurationGuardsTests.cs          # Configuration validation
├── LocalizationTests.cs                 # i18n resource loading (55 tests)
├── LogSanitizerTests.cs                 # Log injection prevention
├── OtpHasherTests.cs                    # Argon2id hashing
├── TranslationModelsTests.cs            # Translation models, status lifecycle (14 tests)
├── TranslationJobQueueTests.cs          # Redis queue operations, FIFO, priority (9 tests)
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
# Output: 86/86 passed (< 5 seconds)
```

### Chat.IntegrationTests (Integration Tests)

**Location**: `tests/Chat.IntegrationTests/`  
**Count**: 70 tests  
**Focus**: End-to-end flows, ChatHub, auth, translation, rate limiting

**Test files**:
```
Chat.IntegrationTests/
├── ChatHubLifecycleTests.cs             # Hub connection, disconnection, groups
├── ImmediatePostAfterLoginTests.cs      # Post-login edge cases
├── MarkReadRateLimitingTests.cs         # Read receipt rate limiting
├── OtpAttemptLimitingTests.cs           # OTP brute-force protection
├── OtpAuthFlowTests.cs                  # Full authentication flow
├── RateLimitingTests.cs                 # Message send rate limiting
├── RoomAuthorizationTests.cs            # Room access control
├── RoomJoinPositiveTests.cs             # Room join scenarios
├── RoomsAuthorizationTests.cs           # Rooms API authorization
└── TranslationServiceIntegrationTests.cs # Azure AI translation (8 tests)
```

**Run integration tests only**:
```bash
dotnet test tests/Chat.IntegrationTests/
# Output: 70/70 passed (< 15 seconds with Azure AI)
```

### Chat.Web.Tests (Web/Security Tests)

**Location**: `tests/Chat.Web.Tests/`  
**Count**: 9 tests  
**Focus**: Health endpoints, security headers, CSP

**Test files**:
```
Chat.Web.Tests/
├── HealthEndpointsTests.cs              # /health and /healthz endpoints
└── SecurityHeadersTests.cs              # CSP, HSTS, X-Frame-Options, etc.
```

**Run web tests only**:
```bash
dotnet test tests/Chat.Web.Tests/
# Output: 9/9 passed (< 3 seconds)
```

## Test Coverage

The test suite covers all critical functionality:

### Core Business Logic (86 unit tests)
- **OTP Generation & Hashing**: Secure random number generation, Argon2id hashing
- **Log Sanitization**: PII removal, log injection prevention (CWE-117)
- **URL Validation**: Local URL detection for secure redirects
- **Configuration Guards**: Validation of required configuration at startup
- **Localization**: Multi-language support validation (culture coverage)
- **Translation Models**: Message translation lifecycle, status tracking (14 tests)
- **Translation Queue**: Redis FIFO queue operations, priority handling (9 tests)

### Integration Flows (70 integration tests)
- **ChatHub Lifecycle**: Connection, room join/leave, message broadcast (14 tests)
- **OTP Authentication**: Full flow from code generation to verification (13 tests)
- **Rate Limiting**: Message send, read receipts, OTP attempts (25 tests)
- **Room Authorization**: Access control, validation (8 tests)
- **Translation Service**: Azure AI integration, caching, multi-language (8 tests)
- **Edge Cases**: Post-login, immediate actions (2 tests)

### Web & Security (9 tests)
- **Health Endpoints**: `/health` and `/healthz` with proper authorization
- **Security Headers**: CSP, HSTS, X-Frame-Options, referrer policy

## Test Organization

Tests are organized by functional area across three projects:

```
tests/
├── Chat.Tests/ (86 unit tests)
│   ├── ConfigurationGuardsTests.cs          # Startup configuration validation
│   ├── LocalizationTests.cs                 # Multi-language support (55 tests)
│   ├── LogSanitizerTests.cs                 # Log security (CWE-117 prevention)
│   ├── OtpHasherTests.cs                    # Cryptographic hashing
│   ├── TranslationModelsTests.cs            # Translation models (14 tests)
│   ├── TranslationJobQueueTests.cs          # Redis queue (9 tests)
│   ├── UnreadNotificationSchedulerTests.cs  # Background jobs
│   └── UrlIsLocalUrlTests.cs                # URL security validation
│
├── Chat.IntegrationTests/ (70 integration tests)
│   ├── ChatHubLifecycleTests.cs             # SignalR hub (14 tests)
│   ├── ImmediatePostAfterLoginTests.cs      # Edge cases (2 tests)
│   ├── MarkReadRateLimitingTests.cs         # Read receipt limits
│   ├── OtpAttemptLimitingTests.cs           # OTP brute-force protection
│   ├── OtpAuthFlowTests.cs                  # Full auth flow
│   ├── RateLimitingTests.cs                 # Message rate limiting
│   ├── RoomAuthorizationTests.cs            # Room access control
│   ├── RoomJoinPositiveTests.cs             # Room join scenarios
│   ├── RoomsAuthorizationTests.cs           # Rooms API authorization
│   └── TranslationServiceIntegrationTests.cs # Azure AI (8 tests)
│
└── Chat.Web.Tests/ (9 web tests)
    ├── HealthEndpointsTests.cs              # Health check endpoints
    └── SecurityHeadersTests.cs              # Security header validation
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
# Output: 202/202 passed ✅
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
# Output: 191/202 passed (11 expected failures) ⚠️
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

## Future Testing Enhancements

Integration tests and end-to-end tests can be implemented in the future when they become a priority. Currently, the focus is on maintaining comprehensive unit test coverage for business logic.

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
- Complex computations
- `Thread.Sleep()` (use `Task.Delay` or `TaskCompletionSource`)
- Excessive setup/teardown

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

### Pair Room Test Fixtures

Tests may use synthetic room names when exercising generic message or translation behavior:
```csharp
public static class TestRooms
{
    public const string PairAlphaBeta = "pair:dc-alpha::dc-beta";
    public const string PairNorthSouth = "pair:dc-north::dc-south";
}
```

Current product behavior is still dispatch-center based. Legacy standard-room names are useful only as isolated test fixture values, not as product configuration.

### Test OTP Codes

In-memory mode logs OTP codes:
```
info: Chat.Web.Services.RedisOtpStore[0]
      OTP code for alice: 123456
```

## Troubleshooting

### Issue: Flaky Tests (Pass/Fail Intermittently)

**Common causes**:
1. **Race conditions** - Use proper synchronization (`await`, locks)
2. **Shared state** - Tests must be independent

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
