# Version 0.9.0 Release - Quick Start Guide

## 📋 Summary

Your SignalR-Chat project is now ready for version **0.9.0** release with a complete versioning strategy in place.

## ✅ What's Been Done

### 1. Version Numbers Updated
- **Chat.Web.csproj**: Set to `0.9.0` with full assembly metadata
- **package.json**: Set to `0.9.0`
- **README.md**: Added version header

### 2. Documentation Created
- **docs/VERSIONING.md**: Complete versioning and release strategy guide
  - Semantic versioning rules
  - Version storage locations
  - Release workflow
  - CI/CD integration details
  - Hotfix process
  - Best practices
- **CHANGELOG.md**: Full changelog for v0.9.0 release
  - Comprehensive list of features, security improvements, and fixes
  - Follows [Keep a Changelog](https://keepachangelog.com/) format

### 3. CI/CD Pipeline Enhanced
- **CD Production workflow** updated to create GitHub Releases automatically
- Release includes:
  - Version tag
  - Deployment confirmation
  - Build metadata (commit SHA, build number)
  - Links to documentation
  - Auto-generated changelog

### 4. Build Verified
✅ All projects build successfully with new version metadata

## 🚀 How to Create v0.9.0 Release

### Step 1: Review Changes
```bash
git status
git diff
```

### Step 2: Commit Version Bump
```bash
git add .
git commit -m "chore: bump version to 0.9.0"
git push origin main
```

### Step 3: Create and Push Tag
```bash
# Create annotated tag
git tag -a v0.9.0 -m "Release v0.9.0 - Initial production release

Major features:
- Complete CI/CD pipeline with GitHub Actions
- OpenTelemetry observability (traces, metrics, logs)
- Argon2id OTP authentication
- Multi-room real-time chat with SignalR
- Read receipts and unread notifications
- Comprehensive test suite (54 tests)
- Azure federated identity (OIDC)
- Health endpoints and monitoring"

# Push tag to trigger production deployment
git push origin v0.9.0
```

### Step 4: What Happens Automatically
1. **GitHub Actions triggers** CD Production workflow
2. **CI runs**: Build + test on windows-latest
3. **Artifacts created**: Deployment package uploaded
4. **Deployment pauses**: Waits for manual approval (production environment protection)
5. **You approve**: Via GitHub UI
6. **Deploys to production**: Azure App Service
7. **GitHub Release created**: Automatically with full changelog

## 📖 Version Files Reference

### Version Information Locations

| File | Current Value | Purpose |
|------|---------------|---------|
| `src/Chat.Web/Chat.Web.csproj` | `0.9.0` | .NET assembly version |
| `package.json` | `0.9.0` | Frontend assets version |
| `README.md` | `0.9.0` | Documentation reference |
| Git tag | `v0.9.0` | Release identifier (to be created) |

## 🔄 Future Releases

### Patch Release (0.9.0 → 0.9.1)
```bash
# Update version in Chat.Web.csproj and package.json
# Update CHANGELOG.md with new fixes
git commit -m "chore: bump version to 0.9.1"
git tag -a v0.9.1 -m "Release v0.9.1 - Bug fixes"
git push origin v0.9.1
```

### Minor Release (0.9.1 → 0.10.0)
```bash
# Update version in Chat.Web.csproj and package.json
# Update CHANGELOG.md with new features
git commit -m "chore: bump version to 0.10.0"
git tag -a v0.10.0 -m "Release v0.10.0 - New features"
git push origin v0.10.0
```

### Major Release (0.10.0 → 1.0.0)
```bash
# Update version in Chat.Web.csproj and package.json
# Update CHANGELOG.md with breaking changes
git commit -m "chore: bump version to 1.0.0"
git tag -a v1.0.0 -m "Release v1.0.0 - First stable release"
git push origin v1.0.0
```

## 📚 Important Files

- **docs/VERSIONING.md** - Complete versioning strategy (read this first!)
- **CHANGELOG.md** - Project history and release notes
- **.github/workflows/cd-production.yml** - Production deployment pipeline
- **README.md** - Project overview with version badge

## ⚠️ Before First Release

Make sure:
- [ ] Azure federated credentials are configured (staging + production environments)
- [ ] GitHub environment `production` exists with required reviewers
- [ ] `AZURE_WEBAPP_NAME` variable is set in production environment
- [ ] Azure secrets are set: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- [ ] All tests pass locally
- [ ] CI builds successfully on main branch

## 🎯 Next Steps

1. **Review the changes** in this commit
2. **Read docs/VERSIONING.md** for detailed strategy
3. **Commit and push** the version bump
4. **Create the v0.9.0 tag** when ready for production
5. **Approve the deployment** in GitHub after tag push

## 🔗 Helpful Commands

```bash
# Check current version in .NET project
grep '<Version>' src/Chat.Web/Chat.Web.csproj

# Check current version in package.json
jq '.version' package.json

# List all existing tags
git tag -l "v*" --sort=-v:refname

# View tag details
git show v0.9.0

# Delete tag if needed (before pushing)
git tag -d v0.9.0

# Delete remote tag (use with caution!)
git push origin --delete v0.9.0
```

## 📞 Support

For questions or issues, refer to:
- **docs/VERSIONING.md** - Comprehensive versioning guide
- **CHANGELOG.md** - Project history
- **.github/workflows/README.md** - CI/CD pipeline documentation

---

**Ready to release?** Follow the steps above to create your first production release! 🚀
