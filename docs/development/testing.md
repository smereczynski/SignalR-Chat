# Testing Guide

This repository keeps a deliberately small test suite.

The goal is to preserve tests that help day-to-day development and remove tests that mainly inflate coverage, duplicate framework behavior, or depend on external services.

## Active Suite

Only one active test project remains:

| Project | Purpose | Speed |
|---------|---------|-------|
| **Chat.Tests** | Focused unit tests for custom logic, policy rules, repositories, and selected service behavior | Fast |

The `tests/Chat.IntegrationTests/` and `tests/Chat.Web.Tests/` folders are not part of the active suite and are not documented as runnable projects.

## Test Selection Rules

Keep a test only if it does one or more of the following:
- protects non-trivial business logic
- catches realistic regressions in custom code
- verifies behavior developers actively change during feature work
- runs locally without secrets, cloud resources, or special setup

Do not keep a test if it mainly:
- reimplements production logic in the test itself
- verifies ASP.NET Core or framework behavior instead of app behavior
- depends on live Azure resources or `.env.local`
- silently returns early instead of reporting a meaningful skipped or failed result

## Running Tests

### Active Test Project

```bash
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo
```

### Filter by Area

```bash
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo --filter "OtpHasher"
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo --filter "AuthControllerTests"
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo --filter "PresenceControllerTests"
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo --filter "HealthEndpointTests"
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo --filter "UnreadNotificationSchedulerTests"
```

### Solution-Level Test Command

```bash
dotnet test src/Chat.sln --no-build --nologo
```

Today this command resolves to the same active test surface as `Chat.Tests`, because the solution currently contains only `Chat.Web` and `Chat.Tests`.

Prefer the project-level command for routine work because it makes the intended scope explicit:

```bash
dotnet test tests/Chat.Tests/Chat.Tests.csproj --no-restore --nologo
```

Use the solution-level command when you specifically want to validate the solution wiring or when a task or CI step already references `src/Chat.sln`.

## Remaining Test Areas

### Security and Validation
- `AuthControllerTests.cs`
- `LogSanitizerTests.cs`
- `OtpHasherTests.cs`
- `ConfigurationGuardsTests.cs`

These cover OTP boundary behavior, custom sanitization, hashing behavior, and startup configuration safeguards.

### Domain and Policy Logic
- `RoomAccessPolicyTests.cs`
- `LanguageCodeTests.cs`
- `PreferredLanguageMergerTests.cs`
- `TranslationFailureClassifierTests.cs`

These cover application-specific rules, normalization, and translation decision logic.

### Repository and Service Behavior
- `InMemoryUsersRepositoryTests.cs`
- `TranslationJobQueueTests.cs`
- `UnreadNotificationSchedulerTests.cs`
- `PresenceCleanupServiceTests.cs`
- `EscalationServiceTests.cs`

These cover behavior that is easy to regress in supporting services and in-memory development infrastructure. The unread notification scheduler tests are now callback-driven and no longer depend on wall-clock delays. The in-memory user repository tests also lock the case-insensitive username and UPN lookup behavior that the Cosmos implementation is expected to mirror.

### Admin Flows
- `AdminDashboardTests.cs`
- `DispatchCentersAdminPageTests.cs`
- `DispatchCentersFeatureTests.cs`
- `EscalationsAdminPageTests.cs`
- `RoomsAdminPageTests.cs`
- `UsersAdminPageTests.cs`

These remain because they exercise repository-backed admin behavior used during ongoing feature work, but the repetitive page-shape checks have been trimmed down to a smaller set of behavioral cases.

### Endpoint Boundaries
- `PresenceControllerTests.cs`
- `HealthEndpointTests.cs`

These cover current controller behavior around presence snapshots, ping and leave handling, response shaping, and the authorization attributes present on the sensitive health endpoints.

### Localization and Claims Helpers
- `LocalizationTests.cs`
- `ClaimsPrincipalExtensionsTests.cs`

These protect resource expectations and helper code that the app uses directly, but the helper coverage is intentionally compact and theory-driven.

## Removed Test Types

The cleanup intentionally removed:
- live integration tests for Azure Translator and Redis
- tests that copied frontend state logic into C#
- tests that only asserted framework URL-helper behavior
- shallow controller tests that did not protect a meaningful external contract
- property bag, serialization, and enum-value tests that only mirrored data-model shape
- duplicated admin flow tests that overlapped stronger feature tests
- timing-based tests that depended on `Task.Delay` instead of invoking the real behavior directly

If those areas need coverage again, add new tests only when they are deterministic, development-useful, and clearly tied to observable app behavior.

## Gaps To Rebuild Properly

The suite is leaner now, so some important areas still need better tests in the future:
- SignalR room and presence behavior through real app-level tests
- middleware behavior around partially-started responses and security headers
- metrics endpoint protection once the endpoint contract is tightened

Those tests should be rebuilt with a clear execution model rather than restoring the removed bloat.