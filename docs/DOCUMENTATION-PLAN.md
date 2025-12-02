# Documentation Restructure Plan

## Goals
1. **Streamline README.md** - Keep it concise (< 500 lines), focus on quickstart and navigation
2. **Organize docs/** - Follow best practices from successful OSS projects (Kubernetes, React, Next.js)
3. **Improve discoverability** - Clear navigation, logical grouping, search-friendly
4. **Maintain quality** - Incorporate existing guides, expand with diagrams, examples
5. **Enable contributors** - Clear contribution guidelines, ADRs for decisions

## Documentation Framework

Following [Diátaxis framework](https://diataxis.fr/):
- **Tutorials** (learning-oriented): getting-started/
- **How-to guides** (problem-oriented): features/, deployment/
- **Reference** (information-oriented): reference/, architecture/
- **Explanation** (understanding-oriented): architecture/decisions/

## New Structure

```
docs/
├── README.md                        # Documentation index
├── getting-started/
│   ├── README.md                   # Getting started overview
│   ├── quickstart.md               # 5-min local setup (in-memory)
│   ├── installation.md             # Full installation guide
│   ├── configuration.md            # Environment variables, options
│   └── first-deployment.md         # Deploy to Azure (simple)
├── architecture/
│   ├── README.md                   # Architecture overview (from ARCHITECTURE.md)
│   ├── system-design.md            # High-level design
│   ├── data-model.md               # Cosmos DB schema, Redis keys
│   ├── security.md                 # Security architecture
│   ├── diagrams/
│   │   ├── runtime-architecture.svg     # App → SignalR → Cosmos/Redis
│   │   ├── infrastructure.svg           # Azure resources
│   │   ├── auth-flow.svg                # OTP authentication flow
│   │   └── message-flow.svg             # Message lifecycle
│   └── decisions/                  # ADRs (Architecture Decision Records)
│       ├── README.md               # ADR index
│       ├── 001-cosmos-db-choice.md
│       ├── 002-redis-for-otp.md
│       ├── 003-signalr-hybrid-mode.md
│       ├── 004-argon2id-hashing.md
│       └── 005-fixed-rooms.md
├── features/
│   ├── README.md                   # Features overview
│   ├── authentication.md           # OTP flow (from GUIDE-OTP-hashing.md)
│   ├── sessions.md                 # Session handling (from GUIDE-Session-handling.md)
│   ├── presence.md                 # Presence tracking (from GUIDE-Visibility.md)
│   ├── real-time-messaging.md      # SignalR implementation
│   ├── read-receipts.md            # Read status tracking
│   ├── notifications.md            # Email/SMS notifications
│   ├── localization.md             # i18n implementation
│   ├── rate-limiting.md            # Rate limiting strategies
│   └── pagination.md               # Message pagination
├── deployment/
│   ├── README.md                   # Deployment overview
│   ├── azure/
│   │   ├── README.md               # Azure deployment guide
│   │   ├── bicep-templates.md      # Bicep IaC (from infra/bicep/README.md)
│   │   ├── app-service.md          # App Service configuration
│   │   ├── networking.md           # VNet, private endpoints
│   │   └── monitoring.md           # Application Insights setup
│   ├── environments.md             # Dev/Staging/Prod configs
│   ├── github-actions.md           # CI/CD (from .github/workflows/README.md)
│   ├── production-checklist.md     # Pre-launch checklist
│   └── troubleshooting.md          # Common deployment issues
├── development/
│   ├── README.md                   # Development overview
│   ├── local-setup.md              # Detailed local setup
│   ├── project-structure.md        # Code organization
│   ├── testing.md                  # Testing guide
│   ├── debugging.md                # Debugging tips
│   ├── vscode-setup.md             # VS Code configuration
│   └── contributing.md             # Link to /CONTRIBUTING.md
├── operations/
│   ├── README.md                   # Operations overview
│   ├── monitoring.md               # Observability overview
│   ├── opentelemetry.md            # OpenTelemetry configuration
│   ├── application-insights.md     # Application Insights setup
│   ├── logging.md                  # Logging best practices
│   ├── diagnostics.md              # Troubleshooting production
│   ├── health-checks.md            # Health endpoint details
│   ├── performance.md              # Performance tuning
│   └── incident-response.md        # Handling outages
├── reference/
│   ├── README.md                   # Reference overview
│   ├── api/
│   │   ├── rest-endpoints.md       # REST API reference
│   │   └── signalr-hub.md          # SignalR hub methods
│   ├── configuration-reference.md  # All config options
│   ├── telemetry-reference.md      # Metrics, traces, logs
│   ├── error-codes.md              # Error reference
│   └── glossary.md                 # Terms and definitions
└── images/                         # Screenshots, diagrams
    ├── hero.gif                    # Demo GIF
    ├── login-page.png
    ├── chat-interface.png
    └── language-picker.png
```

## Migration Plan

### Phase 1: Create Structure
1. ✅ Create new README.md (streamlined)
2. ✅ Create CONTRIBUTING.md
3. ✅ Update CHANGELOG.md
4. Create docs/ directory structure
5. Create placeholder README.md in each section

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
1. Production checklist (security, performance, monitoring)
2. Architecture diagrams (runtime, infrastructure, flows)
3. ADRs for key decisions
4. Quickstart guide (5-minute setup)
5. API reference
6. FAQ

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

- README.md < 500 lines (currently ~650)
- All existing guides migrated
- Zero broken links
- Search-friendly structure
- Clear navigation path for newcomers
- Easy to maintain (logical grouping)

## Timeline

- **Week 1**: Structure + placeholders
- **Week 2**: Migrate existing content
- **Week 3**: Create new content
- **Week 4**: Visual assets + polish
