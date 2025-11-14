# Disaster Recovery Guide

This document describes disaster recovery procedures for the SignalR Chat application, with focus on Cosmos DB data recovery.

## Overview

SignalR Chat implements environment-specific backup strategies to balance data protection with cost optimization:

- **Production**: Continuous backup with 30-day retention (point-in-time restore)
- **Staging**: Periodic backup with 8-hour retention
- **Development**: Periodic backup with 8-hour retention

## Cosmos DB Backup Policy

### Production Environment

**Backup Configuration:**
- **Mode**: Continuous backup (Continuous30Days tier)
- **Frequency**: Automatic backups every 100 seconds
- **Retention**: 30 days
- **RPO** (Recovery Point Objective): ~100 seconds (near-zero data loss)
- **RTO** (Recovery Time Objective): ~1-2 hours (restore operation time)
- **Cost**: ~20% additional RU/s charge (~$70/month for 4000 RU/s)

**Capabilities:**
- Point-in-time restore to any second within last 30 days
- Restore entire account, specific database, or individual container
- Accidental deletion protection
- Data corruption recovery
- Compliance audit support

### Dev/Staging Environments

**Backup Configuration:**
- **Mode**: Periodic backup
- **Frequency**: Every 4 hours
- **Retention**: 8 hours
- **RPO**: 4 hours
- **Cost**: No additional cost (default)

**Capabilities:**
- Restore to last successful periodic backup
- Limited to 8-hour recovery window

## Point-in-Time Restore Procedures

### Prerequisites

1. **Azure Portal Access**: Requires Contributor or Owner role on Cosmos DB account
2. **Target Account Name**: Choose unique name for restored account (cannot overwrite existing)
3. **Restore Timestamp**: Identify exact point in time to restore (UTC timezone)
4. **Scope Decision**: Determine restore scope (account, database, or container)

### Step-by-Step Restore (Azure Portal)

#### 1. Navigate to Cosmos DB Account

```bash
# Get Cosmos DB account name
az cosmosdb list --resource-group rg-signalrchat-prod-plc --query "[].name" -o tsv
```

1. Open [Azure Portal](https://portal.azure.com)
2. Navigate to **Cosmos DB accounts**
3. Select the account to restore from (e.g., `cdb-signalrchat-prod-plc`)

#### 2. Initiate Point-in-Time Restore

1. In left menu, click **Backup & Restore** under **Settings**
2. Click **Restore** button at top
3. Select **Point in time restore**

#### 3. Configure Restore Parameters

**Restore Time:**
- Select date and time (UTC timezone)
- Production: Can select any second within last 30 days
- Dev/Staging: Limited to last 8 hours

**Restore Scope:**
- **Entire Account**: Restore all databases and containers
- **Database**: Restore specific database (e.g., `chat`)
- **Container**: Restore specific container (e.g., `messages`, `users`, `rooms`)

**Target Account:**
- **Subscription**: Select Azure subscription
- **Resource Group**: Choose resource group (can be different from source)
- **Account Name**: Enter unique name (e.g., `cdb-signalrchat-prod-plc-restore-20251114`)
- **Location**: Select Azure region (usually same as source)

**Advanced Options:**
- **Restore with same write region**: Keep enabled for single-region deployments
- **Restore latest backup**: Auto-select most recent backup (alternative to specific timestamp)

#### 4. Validate and Restore

1. Review restore configuration
2. Click **Review + Restore**
3. Verify all parameters are correct
4. Click **Restore**

**Restore Duration:**
- Small databases (<10 GB): 30-60 minutes
- Medium databases (10-100 GB): 1-2 hours
- Large databases (>100 GB): 2-4+ hours

#### 5. Post-Restore Validation

After restore completes:

1. **Verify restored account exists**:
   ```bash
   az cosmosdb show \
     --name cdb-signalrchat-prod-plc-restore-20251114 \
     --resource-group rg-signalrchat-prod-plc
   ```

2. **Check databases and containers**:
   ```bash
   # List databases
   az cosmosdb sql database list \
     --account-name cdb-signalrchat-prod-plc-restore-20251114 \
     --resource-group rg-signalrchat-prod-plc
   
   # List containers in chat database
   az cosmosdb sql container list \
     --account-name cdb-signalrchat-prod-plc-restore-20251114 \
     --resource-group rg-signalrchat-prod-plc \
     --database-name chat
   ```

3. **Query sample data**:
   ```bash
   # Check message count
   az cosmosdb sql container query \
     --account-name cdb-signalrchat-prod-plc-restore-20251114 \
     --database-name chat \
     --container-name messages \
     --query "SELECT VALUE COUNT(1) FROM c"
   ```

4. **Verify timestamp accuracy**: Confirm latest records match restore timestamp

### Step-by-Step Restore (Azure CLI)

```bash
# Set variables
SOURCE_ACCOUNT="cdb-signalrchat-prod-plc"
RESOURCE_GROUP="rg-signalrchat-prod-plc"
RESTORE_TIMESTAMP="2025-11-14T10:30:00Z"  # UTC timezone
RESTORE_ACCOUNT="cdb-signalrchat-prod-plc-restore-$(date +%Y%m%d)"
LOCATION="polandcentral"

# Get source account resource ID
SOURCE_ID=$(az cosmosdb show \
  --name $SOURCE_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --query id -o tsv)

# Restore entire account
az cosmosdb restore \
  --resource-group $RESOURCE_GROUP \
  --name $RESTORE_ACCOUNT \
  --location $LOCATION \
  --restore-source $SOURCE_ID \
  --restore-timestamp $RESTORE_TIMESTAMP

# Restore specific database
az cosmosdb restore \
  --resource-group $RESOURCE_GROUP \
  --name $RESTORE_ACCOUNT \
  --location $LOCATION \
  --restore-source $SOURCE_ID \
  --restore-timestamp $RESTORE_TIMESTAMP \
  --databases-to-restore name=chat

# Restore specific containers
az cosmosdb restore \
  --resource-group $RESOURCE_GROUP \
  --name $RESTORE_ACCOUNT \
  --location $LOCATION \
  --restore-source $SOURCE_ID \
  --restore-timestamp $RESTORE_TIMESTAMP \
  --databases-to-restore name=chat collections=messages,users,rooms
```

## Disaster Recovery Scenarios

### Scenario 1: Accidental Container Deletion

**Problem**: Container `messages` accidentally deleted at 10:45 UTC

**Solution**:
1. Identify deletion timestamp: 10:45 UTC
2. Choose restore timestamp: 10:44 UTC (1 minute before deletion)
3. Restore scope: Database `chat` (includes all containers)
4. After restore completes, migrate data back to production account
5. Delete restored account

**Prevention**: Enable Azure Resource locks on critical containers

### Scenario 2: Data Corruption from Application Bug

**Problem**: Application bug corrupted user records between 14:00-15:00 UTC

**Solution**:
1. Identify last known good timestamp: 13:59 UTC
2. Restore scope: Container `users` only
3. Compare restored data with current data to identify corrupted records
4. Manually fix or bulk update corrupted records
5. Delete restored account

**Prevention**: Implement data validation in application, increase test coverage

### Scenario 3: Compliance Audit Requiring Historical Snapshot

**Problem**: Legal team needs chat messages from specific date/time for audit

**Solution**:
1. Identify required timestamp from audit request
2. Restore scope: Container `messages` for specific room (if possible) or entire `chat` database
3. Export data from restored account
4. Provide exported data to audit team
5. Delete restored account after audit completes

**Retention**: Production continuous backup retains 30 days of history

### Scenario 4: Complete Database Loss (Catastrophic Failure)

**Problem**: Entire Cosmos DB account lost or corrupted

**Solution**:
1. Determine latest known good timestamp
2. Restore entire account to new account name
3. Update application connection strings to point to restored account
4. Verify application functionality
5. If successful, keep restored account as new primary
6. Delete old account after confirming stability

**RTO**: 1-2 hours for restore + 30 minutes for application reconfiguration

## Data Migration After Restore

After restoring to a new Cosmos DB account, you may need to migrate data back to the original account.

### Option 1: Export/Import via Azure Portal

1. **Export from restored account**:
   - Use Data Explorer → Export to JSON
   - Download containers individually

2. **Import to production account**:
   - Use Data Explorer → Import Data
   - Upload JSON files

**Limitations**: Slow for large datasets, manual process

### Option 2: Azure Data Factory

1. Create Azure Data Factory pipeline
2. Configure source: Restored Cosmos DB account
3. Configure sink: Production Cosmos DB account
4. Run copy activity with appropriate filters
5. Validate data integrity

**Benefits**: Fast, automated, supports large datasets

### Option 3: Custom Migration Script

```csharp
// Example: Copy messages container
var sourceClient = new CosmosClient(sourceConnectionString);
var targetClient = new CosmosClient(targetConnectionString);

var sourceContainer = sourceClient.GetContainer("chat", "messages");
var targetContainer = targetClient.GetContainer("chat", "messages");

var iterator = sourceContainer.GetItemQueryIterator<Message>("SELECT * FROM c");

while (iterator.HasMoreResults)
{
    var response = await iterator.ReadNextAsync();
    foreach (var message in response)
    {
        await targetContainer.UpsertItemAsync(message, new PartitionKey(message.RoomId));
    }
}
```

## Restore Cost Considerations

**Continuous Backup Restore Costs:**
- **Restore Request**: One-time charge based on data size (~$0.15/GB)
- **Restored Account**: Standard RU/s charges for new account
- **Storage**: Standard storage charges during restore account lifetime

**Cost Optimization:**
1. Delete restored account as soon as data recovery is complete
2. Restore only required scope (container vs. entire account)
3. Use minimal RU/s provisioning on restored account if read-only access needed

## Backup Monitoring

### Verify Backup Policy

```bash
# Check backup mode for production
az cosmosdb show \
  --name cdb-signalrchat-prod-plc \
  --resource-group rg-signalrchat-prod-plc \
  --query "backupPolicy.type" -o tsv

# Expected output: "Continuous"

# Check continuous tier
az cosmosdb show \
  --name cdb-signalrchat-prod-plc \
  --resource-group rg-signalrchat-prod-plc \
  --query "backupPolicy.continuousModeProperties.tier" -o tsv

# Expected output: "Continuous30Days"
```

### Azure Monitor Alerts

Recommended alerts:
- Restore operation started (informational)
- Restore operation completed (informational)
- Restore operation failed (critical)

## Testing Disaster Recovery

### Quarterly DR Test Procedure

1. **Schedule test**: Announce planned DR test to team
2. **Identify test timestamp**: Use recent timestamp (e.g., 1 hour ago)
3. **Perform test restore**: Restore to temporary account
4. **Validate restored data**: Run data integrity checks
5. **Test data migration**: Practice migrating sample data back
6. **Document results**: Record restore duration, data accuracy
7. **Cleanup**: Delete test restored account
8. **Update runbook**: Incorporate lessons learned

**Recommended Frequency**: Quarterly for production

## Support Contacts

**Azure Support:**
- Create support ticket in Azure Portal
- Select **Technical** issue type
- Choose **Cosmos DB** service
- Priority: **Severity A** for production data loss

**Internal Escalation:**
- Technical Lead: [Contact Info]
- On-call Engineer: [On-call Schedule]
- Azure Account Manager: [Contact Info]

## References

- [Azure Cosmos DB Continuous Backup](https://learn.microsoft.com/azure/cosmos-db/continuous-backup-restore-introduction)
- [Restore from Continuous Backup](https://learn.microsoft.com/azure/cosmos-db/restore-account-continuous-backup)
- [Restore using Azure CLI](https://learn.microsoft.com/azure/cosmos-db/restore-account-continuous-backup?tabs=azure-cli)
- [Backup Cost Estimation](https://learn.microsoft.com/azure/cosmos-db/continuous-backup-restore-introduction#backup-storage-cost)

---

**Last Updated**: 2025-11-14  
**Version**: 1.0  
**Owner**: DevOps Team
