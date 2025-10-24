# Versioning & Release Strategy

## Overview

SignalR-Chat uses **Semantic Versioning (SemVer 2.0.0)** with Git tags as the single source of truth for releases.

**Format**: `vMAJOR.MINOR.PATCH` (e.g., `v0.9.0`, `v1.0.0`, `v1.2.3`)

## Semantic Versioning Rules

Given version `vMAJOR.MINOR.PATCH`:

- **MAJOR**: Breaking changes (incompatible API changes, data model migrations requiring manual intervention)
- **MINOR**: New features, backward-compatible additions
- **PATCH**: Bug fixes, security patches, backward-compatible improvements

### Pre-1.0.0 Versioning

During initial development (0.x.x):
- **0.MINOR.PATCH**: Minor version may include breaking changes
- Use `0.9.x` for release candidates approaching 1.0.0
- `1.0.0` represents the first production-ready release

## Version Storage Locations

### 1. Git Tags (Source of Truth)
- **Format**: `v0.9.0`, `v1.0.0`, `v1.2.3`
- **Purpose**: Triggers CD production pipeline
- **Creation**: Manual via `git tag` or GitHub Releases UI

### 2. .NET Assembly Version
**File**: `src/Chat.Web/Chat.Web.csproj`

```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <Version>0.9.0</Version>
  <AssemblyVersion>0.9.0.0</AssemblyVersion>
  <FileVersion>0.9.0.0</FileVersion>
  <InformationalVersion>0.9.0</InformationalVersion>
</PropertyGroup>
```

- **Version**: NuGet package version (if published)
- **AssemblyVersion**: Strong-name identity (rarely changed to avoid binding issues)
- **FileVersion**: File system version displayed in Windows properties
- **InformationalVersion**: Display version (can include pre-release info like `0.9.0-beta`)

### 3. Frontend Assets Version
**File**: `package.json`

```json
{
  "name": "signalr-chat-assets",
  "version": "0.9.0",
  "private": true
}
```

### 4. Documentation
**File**: `README.md` (optional header)

```markdown
# SignalR-Chat
**Version**: 0.9.0
```

## Release Workflow

### Current Release: v0.9.0 (First Release)

```bash
# 1. Update version in all locations (see below)
# 2. Commit version bump
git add .
git commit -m "chore: bump version to 0.9.0"
git push origin main

# 3. Create and push tag
git tag -a v0.9.0 -m "Release v0.9.0 - Initial production release"
git push origin v0.9.0

# 4. GitHub Actions automatically:
#    - Runs CI (build + test)
#    - Creates deployment artifacts
#    - Requires manual approval (production environment)
#    - Deploys to Azure App Service (production)
#    - Creates GitHub Release with notes
```

### Future Releases

#### Patch Release (e.g., 0.9.0 → 0.9.1)
```bash
# Bug fixes, security patches
git tag -a v0.9.1 -m "Release v0.9.1 - Fix notification delivery bug"
git push origin v0.9.1
```

#### Minor Release (e.g., 0.9.1 → 0.10.0)
```bash
# New features, backward-compatible
git tag -a v0.10.0 -m "Release v0.10.0 - Add message editing feature"
git push origin v0.10.0
```

#### Major Release (e.g., 0.10.0 → 1.0.0)
```bash
# Breaking changes, major milestone
git tag -a v1.0.0 -m "Release v1.0.0 - First stable release"
git push origin v1.0.0
```

## Version Bump Checklist

Before creating a release tag, update version numbers in:

### Required Changes

1. **Chat.Web.csproj**
   ```xml
   <Version>0.9.0</Version>
   <AssemblyVersion>0.9.0.0</AssemblyVersion>
   <FileVersion>0.9.0.0</FileVersion>
   <InformationalVersion>0.9.0</InformationalVersion>
   ```

2. **package.json**
   ```json
   "version": "0.9.0"
   ```

3. **README.md** (optional but recommended)
   Add version badge or header

### Commit Convention
```bash
git commit -m "chore: bump version to X.Y.Z"
```

## CI/CD Integration

### Automated Triggers

| Branch/Tag | Pipeline | Environment | Approval Required |
|------------|----------|-------------|-------------------|
| Any branch | CI only | - | No |
| PR to main | CI + Preview | - | No |
| Push to main | CI + Deploy | Staging | No (auto) |
| Tag `v*.*.*` | CI + Deploy | Production | **Yes (manual)** |

### Deployment Artifacts

Artifacts include version metadata:
- **Build number**: GitHub Actions run number
- **Git SHA**: Commit hash
- **Tag**: Version tag (e.g., `v0.9.0`)
- **Timestamp**: Build timestamp

### GitHub Release Notes

When a version tag is pushed, the CD pipeline should:
1. Build and test
2. Deploy to production (after approval)
3. **Create GitHub Release** with:
   - Tag: `v0.9.0`
   - Title: "Release v0.9.0"
   - Body: Auto-generated changelog or manual notes
   - Artifacts: Deployment package (optional)

## Changelog Management

### Option 1: Automated (Recommended)
Use GitHub's auto-generated release notes:
- Automatically includes merged PRs
- Groups by labels (feature, bug, breaking)
- Acknowledges contributors

### Option 2: Manual CHANGELOG.md
Maintain `CHANGELOG.md` following [Keep a Changelog](https://keepachangelog.com/):

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.9.0] - 2025-10-24

### Added
- CI/CD pipelines with GitHub Actions
- Azure federated identity (OIDC) authentication
- Comprehensive test suite (54 tests)
- OpenTelemetry observability (traces, metrics, logs)
- OTP authentication with Argon2id hashing
- Multi-room real-time chat with SignalR
- Read receipts and unread notifications
- Health endpoints (/healthz, /healthz/ready, /healthz/metrics)

### Security
- Argon2id hashing for OTP codes
- Sanitized logging (no PII in logs)
- Log forging prevention (CWE-117)
- Azure OIDC federated identity (no secrets in CI/CD)

## [0.1.0] - 2025-10-03
### Added
- Initial project structure
- Basic SignalR chat functionality
```

## Hotfix Process

For critical production bugs:

```bash
# 1. Create hotfix branch from production tag
git checkout -b hotfix/0.9.1 v0.9.0

# 2. Fix the bug
git commit -m "fix: critical security vulnerability in OTP validation"

# 3. Update version to 0.9.1
# Edit Chat.Web.csproj, package.json

# 4. Commit version bump
git commit -m "chore: bump version to 0.9.1"

# 5. Merge to main
git checkout main
git merge hotfix/0.9.1
git push origin main

# 6. Tag and release
git tag -a v0.9.1 -m "Hotfix v0.9.1 - Security: Fix OTP validation bypass"
git push origin v0.9.1

# 7. Clean up
git branch -d hotfix/0.9.1
```

## Version Metadata at Runtime

### Expose Version in Application

**Add to Startup.cs** (in `ConfigureServices`):
```csharp
services.Configure<ApplicationMetadata>(options =>
{
    options.Version = typeof(Startup).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "unknown";
    options.BuildTime = DateTime.UtcNow; // Or read from build artifact
});
```

**Display in UI** (e.g., footer):
```html
<footer>
  <small>SignalR-Chat v@ViewData["Version"]</small>
</footer>
```

**Expose via Health Endpoint**:
```json
{
  "status": "Healthy",
  "version": "0.9.0",
  "buildNumber": "123",
  "gitSha": "5d4c445"
}
```

## Pre-release Versions

For beta/RC releases:

```bash
# Beta releases
git tag -a v1.0.0-beta.1 -m "Release v1.0.0-beta.1"

# Release candidates
git tag -a v1.0.0-rc.1 -m "Release v1.0.0-rc.1"
```

Update `InformationalVersion`:
```xml
<InformationalVersion>1.0.0-beta.1</InformationalVersion>
```

## Best Practices

1. **Never reuse tags**: Once pushed, tags are immutable
2. **Always annotate tags**: Use `git tag -a` for release metadata
3. **Version bump commits**: Use `chore:` prefix, separate from features
4. **Test before tagging**: Ensure CI passes on main before creating release tag
5. **Document breaking changes**: Always note breaking changes in release notes
6. **Sync versions**: Keep .csproj and package.json versions in sync
7. **Atomic releases**: Version bump + tag push should be close together

## Migration to 1.0.0

Criteria for 1.0.0 release:
- [ ] All core features complete and tested
- [ ] Production deployment validated
- [ ] Performance benchmarked
- [ ] Security audit completed
- [ ] Documentation complete
- [ ] Breaking changes stabilized
- [ ] API surface frozen

## Tools

### Check Current Version
```bash
# Git tags
git describe --tags --abbrev=0

# .NET assembly
dotnet --version # SDK version
grep '<Version>' src/Chat.Web/Chat.Web.csproj

# Frontend
jq '.version' package.json
```

### List All Releases
```bash
git tag -l "v*" --sort=-v:refname
```

### Delete Tag (if needed)
```bash
# Local
git tag -d v0.9.0

# Remote (CAUTION: May break deployments)
git push origin --delete v0.9.0
```

## References

- [Semantic Versioning 2.0.0](https://semver.org/)
- [Keep a Changelog](https://keepachangelog.com/)
- [Conventional Commits](https://www.conventionalcommits.org/)
- [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github)
