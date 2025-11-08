# Redis Module - ARM Template Workaround

## Why ARM instead of Bicep?

This module uses a **pure ARM template** (array-based) instead of Bicep due to a bug in Azure Redis Enterprise Resource Provider.

### The Bug

**Issue**: Redis Enterprise Resource Provider has THREE critical bugs:
1. Cannot parse ARM templates with `"languageVersion": "2.0"`
2. `listKeys` operation requires `accessKeysAuthentication: "Enabled"` on database (defaults to `Disabled` in API @2025-07-01)
3. **Cluster-level `listKeys` endpoint is BROKEN** - must use database-level endpoint instead

**Affected API Versions**: ALL (including @2020-10-01-preview through @2025-07-01)  
**Error Messages**: 
- `"The localPath field is required."` (misleading - this property doesn't exist)
- `"The ListKeys operation is not supported when access keys are disabled."`

**Root Causes**:
1. When Bicep modules use outputs with symbolic name references (e.g., `redisEnterprise.properties.hostName`), Bicep compiles to Language Version 2.0 format where resources are stored as an object instead of an array. The Redis Enterprise Resource Provider cannot parse this format.
2. API @2025-07-01 changed the default for `accessKeysAuthentication` from `Enabled` to `Disabled`, breaking `listKeys` operations unless explicitly set to `Enabled`.
3. **Cluster-level listKeys endpoint (`/redisEnterprise/{name}/listKeys`) is completely broken**. The correct endpoint is `/redisEnterprise/{name}/databases/{dbName}/listKeys`.

### Evidence

**Working**:
- ✅ Direct `az rest` API calls
- ✅ Standard ARM templates (resources as array)
- ✅ Bicep files without outputs/symbolic references

**Failing**:
- ❌ Bicep modules with outputs using symbolic names
- ❌ ARM templates with `"languageVersion": "2.0"`
- ❌ Nested deployments using Language Version 2.0

### References

- **Microsoft Learn - Redis Enterprise API**: https://learn.microsoft.com/en-us/azure/templates/microsoft.cache/redisenterprise
- **Bicep Language Version 2.0**: https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/file
- **Report Issues**: https://aka.ms/bicep-type-issues

### Files

- **`redis-arm.json`**: Pure ARM template (array-based, no Language Version 2.0)
- **`redis.bicep`**: Original Bicep module (kept for reference, compiles to Language Version 2.0)
- **`redis.json`**: Compiled output of redis.bicep (Language Version 2.0, BROKEN)

### Template Details

**API Version**: `2025-07-01` (latest stable)

**Features**:
- ✅ Redis Enterprise cluster
- ✅ Default database (port 10000)
- ✅ Private endpoint support
- ✅ Environment-based SKU (B1/B3/B5)
- ✅ Public Network Access control (`publicNetworkAccess: "Enabled"` - required in @2025-07-01)
- ✅ **Access Keys Authentication** (`accessKeysAuthentication: "Enabled"` - CRITICAL for listKeys)
- ❌ High Availability (property exists but causes Language Version 2.0 compilation issues)

### Usage

The ARM template is called from `main.bicep` as a module:

```bicep
module redis './modules/redis-arm.json' = {
  name: 'redis-deployment'
  params: {
    redisName: 'redis-${baseName}-${environment}-${shortLocation}'
    location: location
    environment: environment
    privateEndpointSubnetId: networking.outputs.privateEndpointsSubnetId
  }
}
```

### When to Update

Monitor Microsoft's Bicep releases and Azure Resource Provider updates. Once the bug is fixed:

1. Switch back to `redis.bicep` in `main.bicep`
2. Update to latest API version (e.g., @2025-05-01-preview)
3. Add High Availability and Public Network Access properties
4. Test deployment
5. Delete `redis-arm.json` if no longer needed

### Verification

You can verify the module format with:

```bash
cd infra/bicep
az bicep build --file main.bicep --stdout | \
  jq '.resources.redis.properties.template | {languageVersion, resourcesType: (.resources | type)}'
```

**Expected output**:
```json
{
  "languageVersion": null,
  "resourcesType": "array"
}
```

This confirms the Redis module uses standard ARM format (array-based resources) without Language Version 2.0.
