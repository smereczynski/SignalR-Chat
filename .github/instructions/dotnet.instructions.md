---
applyTo: "**/*.{cs,csproj,props,targets,sln,json,yml,yaml}"
---

# .NET 10 Review Guidance

## Scope
Apply these rules to .NET 10 application code, tests, project files, configuration, and CI files.

## C# and .NET review focus
- Check nullable reference type correctness and whether warnings are suppressed unsafely.
- Check async/await usage, ConfigureAwait needs, cancellation token propagation, IAsyncDisposable, and stream/database disposal.
- Check DI lifetimes for captive dependencies, singleton misuse, scoped service leakage, and service locator patterns.
- Check Options binding and configuration for missing validation, dangerous defaults, and secrets handling.
- Check logging for structured logging, PII leakage, token leakage, and exception handling quality.
- Check exception handling boundaries: avoid catch-all swallowing, leaked internals, and inconsistent mapping to HTTP/problem details.
- Check LINQ and EF Core usage for N+1 queries, client-side evaluation risks, tracking misuse, Include overuse, missing indexes implied by queries, and transaction consistency.
- Check ASP.NET Core authn/authz: policy use, claim assumptions, endpoint exposure, CORS, cookie/JWT settings, antiforgery where relevant, and rate-limiting gaps.
- Check model validation, DTO-to-domain mapping, overposting/mass assignment risks, and trust of client-supplied identifiers or roles.
- Check serialization settings for polymorphism, reference loops, enum/string mismatch, timezone handling, and unsafe converters.
- Check concurrency assumptions around caches, static state, background services, and parallel processing.
- Check DateTime/DateTimeOffset usage, UTC consistency, and culture-sensitive parsing/formatting.
- Check domain logic for invariant enforcement instead of relying only on controller-level validation.

## Project and config files
- Review csproj/props/targets for unsafe package versions, broad wildcards, analyzer suppression, disabled warnings, and publish settings that weaken security.
- Review appsettings*.json and environment config for insecure defaults, debug flags, permissive CORS, verbose errors, and misplaced secrets.
- Review CI files for missing restore/build/test/analyzer/security steps.

## Testing expectations
- Risky code paths should have tests for unauthorized access, malformed input, null/empty input, cancellation, retries, idempotency, and failure rollback.
- For bug-prone logic, suggest the minimum regression test needed.

## Severity guidance
- Critical: exploitable security issue, authorization bypass, destructive data corruption, or guaranteed production failure.
- High: likely bug or security weakness with meaningful impact.
- Medium: maintainability or reliability issue likely to cause future defects.
- Low: clarity issue with modest impact.
