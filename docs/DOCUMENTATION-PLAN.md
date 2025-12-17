# Documentation Restructure Plan

## Goals
1. **Streamline README.md** - Keep it concise (< 500 lines), focus on quickstart and navigation
2. **Organize docs/** - Follow Diátaxis-style grouping (getting-started / features / architecture / deployment / operations / reference)
3. **Improve discoverability** - Clear navigation, logical grouping, search-friendly
4. **Maintain quality** - Keep docs code-backed and up to date
5. **Enable contributors** - Clear contribution guidelines, ADRs for decisions

## Documentation Framework

Following [Diátaxis framework](https://diataxis.fr/):
- **Tutorials** (learning-oriented): getting-started/
- **How-to guides** (problem-oriented): features/, deployment/
- **Reference** (information-oriented): reference/, architecture/
- **Explanation** (understanding-oriented): architecture/decisions/

## Current Structure (as of 2025-12-17)

```
docs/
├── README.md
├── getting-started/
│   ├── README.md
│   ├── quickstart.md
│   ├── installation.md
│   └── configuration.md
├── architecture/
│   ├── overview.md
│   ├── system-design.md
│   ├── translation-architecture.md
│   ├── data-model.md
│   └── decisions/
│       ├── 0001-signalr-cors-origin-validation.md
│       ├── 0002-cosmos-db-continuous-backup.md
│       └── 0003-login-sso-email-otp.md
├── features/
│   ├── README.md
│   ├── authentication.md
│   ├── sessions.md
│   ├── presence.md
│   └── async-translation-implementation-plan.md
├── deployment/
│   ├── README.md
│   ├── bootstrap.md
│   ├── production-checklist.md
│   ├── github-actions.md
│   ├── github-secrets.md
│   ├── github-secrets-setup.md
│   ├── github-variables.md
│   ├── post-deployment-manual-steps.md
│   ├── windows-to-linux-migration.md
│   ├── azure-translation.md
│   ├── translation-environment-configuration.md
│   └── azure/
│       ├── README.md
│       └── bicep-templates.md
├── development/
│   ├── local-setup.md
│   ├── testing.md
│   ├── entra-id-multi-tenant-setup.md
│   ├── admin-panel-app-role-integration.md
│   └── integration-tests-improvements.md
├── operations/
│   ├── monitoring.md
│   └── disaster-recovery.md
└── reference/
    └── faq.md
```

## Migration Plan

### Phase 1: Create Structure
1. ✅ Create new README.md (streamlined)
2. ✅ Create CONTRIBUTING.md
3. ✅ Update CHANGELOG.md
4. ✅ Create docs/ directory structure
5. ⚠️ Create placeholder README.md in each section (partially complete)

### Phase 2: Migrate Existing Content
1. **ARCHITECTURE.md** → `docs/architecture/README.md` + split into:
   - `system-design.md` (overview)
   - `data-model.md` (Cosmos schema)
   - `security.md` (security details)
2. **docs/GUIDE-OTP-hashing.md** → `docs/features/authentication.md`
3. **docs/GUIDE-Session-handling.md** → `docs/features/sessions.md`
6. **docs/GUIDE-Visibility.md** → `docs/features/presence.md`
7. **docs/BOOTSTRAP.md** → `docs/development/local-setup.md` (section)
8. ✅ **.github/workflows/README.md** → `docs/deployment/github-actions.md` (CONSOLIDATED Dec 2, 2025)
9. ✅ **infra/bicep/README.md** → `docs/deployment/azure/bicep-templates.md` (MIGRATED Dec 2, 2025)

### Phase 3: Create New Content
1. ✅ Production checklist (security, performance, monitoring)
2. Architecture diagrams (runtime, infrastructure, flows)
3. ✅ ADRs for key decisions (initial set)
4. ✅ Quickstart guide (5-minute setup)
5. API reference
6. ✅ FAQ

### Phase 4: Add Visual Assets
1. Screenshot: Login page
2. Screenshot: Chat interface with read receipts
3. Screenshot: Language picker
4. GIF: Demo showing reconnection
5. Diagram: Runtime architecture
6. Diagram: OTP authentication flow

### Phase 5: Polish & Review
1. Review all links (internal navigation)
2. Add "Edit on GitHub" links
3. Spell check all markdown
4. Test all code samples
5. Get feedback from contributors

## Key Improvements from Review

✅ **5-minute quickstart** - In README.md, no Azure required
✅ **Hero image/GIF** - Visual demonstration
✅ **.env.local.example** - In `docs/getting-started/configuration.md`
✅ **Architecture diagram** - Simple runtime diagram in README
✅ **Production checklist** - Dedicated doc with links
✅ **Why/Non-goals** - Clear scope in README
✅ **Contributing guide** - CONTRIBUTING.md created
✅ **License visibility** - In README header + footer
✅ **Project layout tree** - In README
✅ **Shorter diagnostics** - Move to docs/, summary in README

## Success Metrics

- README.md < 500 lines (currently ~360)
- All existing guides migrated (or intentionally retired)
- Zero broken links in README.md and docs index
- Search-friendly structure
- Clear navigation path for newcomers
- Easy to maintain (logical grouping)

## Timeline

- **Week 1**: Structure + placeholders
- **Week 2**: Migrate existing content
- **Week 3**: Create new content
- **Week 4**: Visual assets + polish
