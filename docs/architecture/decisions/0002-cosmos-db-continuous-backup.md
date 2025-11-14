# ADR 0002: Cosmos DB Continuous Backup Policy for Production

**Date**: 2025-11-14  
**Status**: Accepted  
**Issue**: [#104 - Configure Continuous Backup Policy for Production Cosmos DB](https://github.com/smereczynski/SignalR-Chat/issues/104)

## Context

SignalR Chat stores all chat messages, user profiles, and room data in Azure Cosmos DB. Production data requires protection against accidental deletion, data corruption, and compliance requirements for data recovery.

### The Problem

Before this decision:
- **Default Periodic backup**: 8-hour backup interval with 8-hour retention
- **RPO (Recovery Point Objective)**: 8 hours of potential data loss
- **No point-in-time restore**: Cannot restore to specific timestamp
- **Limited compliance**: Insufficient for production-grade data protection
- **Risk exposure**: Users could lose up to 8 hours of chat messages, read receipts, presence data

### Requirements

1. **Near-zero data loss**: Production RPO < 2 minutes
2. **Point-in-time restore**: Ability to restore to any second within retention period
3. **Accidental deletion protection**: Recover from human errors quickly
4. **Compliance**: Meet 30-day audit trail requirements
5. **Cost optimization**: Balance protection with budget constraints
6. **Environment-specific**: Different policies for dev/staging/prod

## Decision

We will implement **environment-specific backup policies** with Continuous backup for production and Periodic backup for non-production environments.

### Production Environment: Continuous Backup

**Configuration:**
```bicep
backupPolicy: {
  type: 'Continuous'
  continuousModeProperties: {
    tier: 'Continuous30Days'
  }
}
```

**Specifications:**
- **Backup frequency**: Every 100 seconds (automatic)
- **Retention**: 30 days
- **RPO**: ~100 seconds (near-zero data loss)
- **Restore capability**: Point-in-time to any second within 30 days
- **Restore scope**: Entire account, specific database, or individual container
- **Cost**: ~20% additional RU/s charge (~$70/month for 4000 RU/s autoscale)

### Dev/Staging Environments: Periodic Backup

**Configuration:**
```bicep
backupPolicy: {
  type: 'Periodic'
  periodicModeProperties: {
    backupIntervalInMinutes: 240  // 4 hours
    backupRetentionIntervalInHours: 8
    backupStorageRedundancy: 'Local'
  }
}
```

**Specifications:**
- **Backup frequency**: Every 4 hours
- **Retention**: 8 hours (2 backups)
- **RPO**: 4 hours
- **Restore capability**: Last successful periodic backup only
- **Cost**: No additional cost (default)

## Alternatives Considered

### Alternative 1: Continuous Backup for All Environments

**Pros:**
- Consistent experience across environments
- Better dev/staging data protection
- Easier testing of restore procedures

**Cons:**
- **Cost**: ~$210/month additional (3 environments × $70)
- **Unnecessary**: Dev/staging data is not critical
- **Complexity**: Overkill for development workflows

**Decision**: Rejected - not cost-effective for non-production environments

### Alternative 2: Periodic Backup with Shorter Intervals (Production)

**Configuration**: Periodic with 1-hour interval, 24-hour retention

**Pros:**
- Lower cost than Continuous (~10% vs ~20% RU increase)
- Better than default 8-hour interval
- Simpler restore process

**Cons:**
- **1-hour RPO**: Still significant data loss window for chat application
- **No point-in-time restore**: Cannot restore to exact timestamp
- **Limited audit capability**: 24-hour retention insufficient for compliance
- **Coarse granularity**: Cannot recover from events within 1-hour window

**Decision**: Rejected - 1-hour data loss unacceptable for production chat app

### Alternative 3: Continuous7Days Tier (Lower-Cost Option)

**Configuration**: Continuous backup with 7-day retention

**Pros:**
- Lower cost than Continuous30Days (~15% vs ~20% RU increase)
- Still provides point-in-time restore
- Same 100-second backup frequency

**Cons:**
- **7-day retention**: Insufficient for compliance audits
- **Limited investigation window**: Cannot analyze data older than 7 days
- **Marginal savings**: Only ~$15/month difference vs 30-day tier
- **Operational risk**: Shorter window for identifying and recovering from issues

**Decision**: Rejected - 30-day retention provides better compliance and operational flexibility

### Alternative 4: Third-Party Backup Solution

**Examples**: Veeam for Azure, CloudRanger, Azure Backup (when supported)

**Pros:**
- Additional backup redundancy
- Cross-cloud backup capabilities
- Advanced retention policies

**Cons:**
- **Additional cost**: Service fees + storage costs
- **Complexity**: Additional tooling and management
- **Vendor dependency**: Reliance on third-party service
- **Native alternative**: Cosmos DB Continuous backup provides sufficient capabilities

**Decision**: Rejected - native Azure solution is sufficient and simpler

## Consequences

### Positive Consequences

✅ **Data Protection:**
- Near-zero data loss for production (RPO: ~100 seconds)
- Point-in-time restore to any second within 30 days
- Protection against accidental deletions and data corruption

✅ **Compliance:**
- 30-day audit trail meets regulatory requirements
- Ability to provide historical snapshots for investigations
- Documented disaster recovery procedures

✅ **Operational Benefits:**
- Fast recovery from human errors (1-2 hour RTO)
- Granular restore scope (account, database, or container)
- Automated backups (no manual intervention required)

✅ **Cost Optimization:**
- Production only: $70/month additional cost
- Dev/Staging: No additional cost (Periodic backup is default)
- Total impact: ~$70/month for production-grade data protection

✅ **Developer Experience:**
- Dev/staging environments maintain fast deployment cycles
- No backup overhead during development
- Production parity where it matters (data protection model)

### Negative Consequences

❌ **Cost Impact:**
- $70/month additional cost for production Cosmos DB
- ~20% increase in RU/s billing (4000 RU/s → 4800 RU/s effective)
- Restore operations incur one-time charges (~$0.15/GB)

❌ **Restore Complexity:**
- Restored data goes to new account (cannot overwrite existing)
- Requires manual data migration or connection string updates
- Testing restore procedures requires periodic DR drills

❌ **Operational Overhead:**
- Need documented disaster recovery procedures
- Team training on restore procedures required
- Quarterly DR testing recommended

## Implementation

### Bicep Configuration

**File**: `infra/bicep/modules/cosmos-db.bicep`

```bicep
// Backup policy: Continuous (30 days) for production, Periodic for dev/staging
var backupPolicy = environment == 'prod' ? {
  type: 'Continuous'
  continuousModeProperties: {
    tier: 'Continuous30Days'
  }
} : {
  type: 'Periodic'
  periodicModeProperties: {
    backupIntervalInMinutes: 240  // 4 hours
    backupRetentionIntervalInHours: 8
    backupStorageRedundancy: 'Local'
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: consistencyPolicy
    locations: locations
    enableAutomaticFailover: false
    enableMultipleWriteLocations: false
    publicNetworkAccess: privateEndpointSubnetId != '' ? 'Disabled' : 'Enabled'
    capabilities: []
    backupPolicy: backupPolicy  // Environment-specific backup policy
  }
}
```

### Deployment

Infrastructure deployed via GitHub Actions workflow:
- **Dev environment**: Periodic backup (4-hour interval)
- **Staging environment**: Periodic backup (4-hour interval)
- **Production environment**: Continuous backup (Continuous30Days tier)

### Documentation

1. **Backup Policy**: Documented in `infra/bicep/README.md`
2. **Disaster Recovery**: Comprehensive guide in `docs/operations/disaster-recovery.md`
3. **Restore Procedures**: Step-by-step instructions for Azure Portal and CLI
4. **Cost Analysis**: Production cost impact documented in CHANGELOG.md

### Monitoring

**Recommended Azure Monitor Alerts:**
- Restore operation started (informational)
- Restore operation completed (success)
- Restore operation failed (critical)

**Verification:**
```bash
# Verify production backup mode
az cosmosdb show \
  --name cdb-signalrchat-prod-plc \
  --resource-group rg-signalrchat-prod-plc \
  --query "backupPolicy.type" -o tsv

# Expected: "Continuous"
```

## Testing Strategy

### Pre-Production Validation

1. **Dev deployment**: Verify Periodic backup configuration
2. **Staging deployment**: Verify Periodic backup configuration  
3. **Production deployment**: Verify Continuous30Days tier
4. **Azure Portal validation**: Confirm backup settings in portal

### Disaster Recovery Testing

**Quarterly DR Drill:**
1. Identify test restore timestamp (1 hour ago)
2. Restore to temporary account
3. Validate data integrity
4. Test data migration procedures
5. Measure restore duration
6. Document results and lessons learned
7. Delete temporary account

**First DR Test After Implementation:**
- Schedule within 30 days of production deployment
- Full team walkthrough of restore procedures
- Validate documentation accuracy
- Update runbooks based on findings

## Rollback Plan

If Continuous backup causes issues:

1. **Immediate rollback** (Bicep):
   ```bicep
   backupPolicy: {
     type: 'Periodic'
     periodicModeProperties: {
       backupIntervalInMinutes: 60  // 1 hour
       backupRetentionIntervalInHours: 24
       backupStorageRedundancy: 'Geo'  // Better than Local
     }
   }
   ```

2. **Redeploy infrastructure**: Via GitHub Actions
3. **Cost savings**: Revert to no additional RU/s charges
4. **Trade-off**: Accept 1-hour RPO and 24-hour retention

**Note**: Rollback requires infrastructure redeployment; cannot change backup mode on existing account.

## References

- [Azure Cosmos DB Continuous Backup](https://learn.microsoft.com/azure/cosmos-db/continuous-backup-restore-introduction)
- [Backup Policy Configuration in Bicep](https://learn.microsoft.com/azure/cosmos-db/provision-account-continuous-backup)
- [Backup Tiers Comparison](https://learn.microsoft.com/azure/cosmos-db/continuous-backup-restore-introduction#backup-tiers)
- [Point-in-Time Restore](https://learn.microsoft.com/azure/cosmos-db/restore-account-continuous-backup)
- [Backup Cost Estimation](https://learn.microsoft.com/azure/cosmos-db/continuous-backup-restore-introduction#backup-storage-cost)

## Related Decisions

- [ADR 0001: SignalR CORS and Origin Validation](./0001-signalr-cors-origin-validation.md) - Security decision protecting chat data
- [Issue #84: Infrastructure as Code Implementation](https://github.com/smereczynski/SignalR-Chat/issues/84) - Bicep templates foundation
- [Issue #93: Infrastructure Deployment](https://github.com/smereczynski/SignalR-Chat/issues/93) - Deployment validation

## Future Considerations

### Potential Enhancements

1. **Multi-region failover**: Add secondary region with automatic failover
2. **Backup encryption**: Customer-managed keys for backup encryption
3. **Cross-region restore**: Restore to different Azure region for disaster recovery
4. **Automated restore testing**: Scheduled automated DR drills
5. **Backup retention extension**: Increase to Continuous90Days if compliance requires

### Triggers for Reassessment

- **Cost constraints**: If budget pressures require cost reduction
- **Compliance changes**: If retention requirements increase (e.g., 90 days)
- **Data growth**: If dataset exceeds 1 TB (restore time considerations)
- **Multi-region deployment**: If application expands to multiple regions
- **Regulatory requirements**: If industry regulations mandate specific backup policies

---

**Decision Made By**: DevOps Team  
**Approved By**: Technical Lead  
**Implementation Date**: 2025-11-14  
**Next Review**: 2026-02-14 (3 months)
