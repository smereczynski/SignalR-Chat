# Production Deployment Checklist

Use this checklist before deploying SignalR Chat to production to ensure security, performance, and reliability.


## GitHub Actions Setup

**Required:**
- [GitHub Variables Guide](github-variables.md)
- [GitHub Secrets Guide](github-secrets.md)

Ensure all required variables and secrets are set for your environment before production deployment.

---

## 🔒 Security

### Authentication & Secrets

- [ ] **Set OTP Pepper** (high-entropy Base64 secret)
  ```bash
  # Generate pepper (32 bytes = 44 Base64 characters)
  openssl rand -base64 32
  ```
  - Set in Azure: App Service → Configuration → Application Settings
  - Key: `Otp__Pepper`
  - ⚠️ **NEVER** commit to source control
  - 📖 [Authentication guide](../features/authentication.md)

- [ ] **Configure Connection Strings in Azure**
  - Use **Connection Strings** section (not Application Settings)
  - Injected as `CUSTOMCONNSTR_{name}` environment variables
  - Configure:
    - `Cosmos` - Cosmos DB connection string
    - `Redis` - Redis connection string
    - `AzureCommunicationServices` - ACS connection string (optional)
    - `AzureSignalR` - SignalR Service connection string (optional)
  - 📖 [Configuration guide](../getting-started/configuration.md#connection-strings)

- [ ] **Application Insights Connection String**
  - Set `APPLICATIONINSIGHTS_CONNECTION_STRING` in App Service
  - Enables telemetry in production
  - 📖 [Monitoring & observability](../operations/monitoring.md)

### Security Headers

- [ ] **Verify HSTS is enabled** (Production environment only)
  - Expected header: `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`
  - Test: `curl -I https://your-app.azurewebsites.net`
  - 📖 [HSTS configuration](../features/sessions.md#hsts)

- [ ] **Verify CSP headers** (all environments)
  - Check `Content-Security-Policy` header includes nonce
  - Verify no `unsafe-eval` in policy
  - 📖 [CSP guide](../features/sessions.md#content-security-policy)

### Access Control

- [ ] **Disable public access to Cosmos DB**
  - Enable private endpoints only
  - Verify: Cosmos DB → Networking → Public access: Disabled

- [ ] **Disable public access to Redis**
  - Enable private endpoints only
  - Verify: Redis → Networking → Public access: Disabled

- [ ] **Configure SignalR Service network ACLs**
  - Public endpoint: ClientConnection only (no ServerConnection)
  - Private endpoint: All traffic types
  - 📖 [Azure deployment](azure/README.md)

## ⚙️ Configuration

### Rate Limiting

- [ ] **OTP endpoint rate limiting**
  - Verify: auth endpoints are protected by the ASP.NET fixed-window rate limiter
  - Verify: request bursts can return HTTP 429
  - Test with: `hey -n 30 -c 10 https://your-app.azurewebsites.net/api/auth/start`
  - 📖 [Authentication guide](../features/authentication.md#rate-limiting)

- [ ] **OTP attempt limiting**
  - Verify: 5 failed attempts per user
  - Verify: 5-minute cooldown period
  - Config: `Otp__MaxAttempts` (default: 5)
  - 📖 [Authentication guide](../features/authentication.md#rate-limiting)

### TTL & Cleanup

- [ ] **Configure message TTL**
  - Set `Cosmos__MessagesTtlSeconds` for automatic message expiration
  - Options:
    - `null` - TTL disabled (default)
    - `-1` - TTL enabled but never expire
    - `>0` - Expire after N seconds (e.g., 2592000 = 30 days)
  - 📖 [Cosmos TTL configuration](../architecture/data-model.md#message-ttl)

### Notification Settings (Optional)

- [ ] **Configure Azure Communication Services** (if using SMS/email)
  - Set `CUSTOMCONNSTR_AzureCommunicationServices`
  - Set `Acs__EmailFrom` (email address)
  - Set `Acs__SmsFrom` (phone number)
  - Verify sender identities in ACS
  - 📖 [Configuration guide](../getting-started/configuration.md#notification-configuration)

- [ ] **Configure notification delay**
  - Set `Notifications__UnreadDelaySeconds` (default: 60)
  - Lower for urgent notifications, higher for less noise
  - 📖 [Configuration guide](../getting-started/configuration.md#notification-configuration)

## 🏥 Health & Monitoring

### Health Checks

- [ ] **Configure health probes**
  - Liveness: `/healthz` (responds 200 OK always)
  - Readiness: `/healthz/ready` (checks Cosmos + Redis)
  - Set in: App Service → Health check → Path: `/healthz/ready`
  - 📖 [Monitoring & observability](../operations/monitoring.md#7-health--diagnostics-endpoints)

- [ ] **Verify health check intervals**
  - App Service default: 1-minute interval
  - Recommendation: 30 seconds for critical apps
  - 📖 [Azure health check docs](https://learn.microsoft.com/azure/app-service/monitor-instances-health-check)

### Monitoring

- [ ] **Enable Application Insights**
  - Set `APPLICATIONINSIGHTS_CONNECTION_STRING`
  - Verify logs appear in Azure Monitor
  - 📖 [Monitoring & observability](../operations/monitoring.md#21-application-insights--log-analytics)

- [ ] **Configure log retention**
  - Log Analytics: 30d (dev), 90d (staging), 365d (prod)
  - Set in: Log Analytics Workspace → Usage and estimated costs
  - 📖 [Monitoring & observability](../operations/monitoring.md#9-production-guidance)

- [ ] **Set up alerts** (recommended)
  - High error rate (>5% in 5 minutes)
  - High CPU (>80% for 10 minutes)
  - Health check failures (3 consecutive)
  - Cosmos DB throttling (429 responses)
  - Redis connection failures
  - 📖 [Monitoring & observability](../operations/monitoring.md)

## 🚀 Performance

### App Service

- [ ] **Choose appropriate SKU**
  - Dev: P0V4 (1 vCore, 3.5 GB RAM)
  - Staging: P0V4 (1 vCore, 3.5 GB RAM)
  - Production: P0V4+ (scale out horizontally)
  - 📖 [Azure deployment](azure/README.md)

- [ ] **Enable App Service VNet integration**
  - Verify: App Service → Networking → VNet integration
  - Ensures traffic routes through private endpoints
  - 📖 [Azure deployment](azure/README.md)

### Cosmos DB

- [ ] **Choose provisioning mode**
  - Dev: Serverless (pay per request)
  - Staging: Standard (1000 RU/s manual)
  - Production: Standard (4000 RU/s autoscale)
  - 📖 [Cosmos DB provisioning](../architecture/data-model.md#throughput)

- [ ] **Verify partition key**
  - Messages container: `/roomId`
  - Users container: `/userName`
  - Rooms container: `/id`
  - 📖 [Partition strategy](../architecture/data-model.md#partitioning)

### Redis

- [ ] **Choose appropriate tier**
  - Dev: Balanced_B1 (2 GB)
  - Staging: Balanced_B3 (6 GB)
  - Production: Balanced_B5 (26 GB)
  - Enable clustering for high availability
  - 📖 [Azure deployment](azure/README.md)

### Azure SignalR Service

- [ ] **Choose appropriate tier** (if using)
  - All environments: Standard_S1 (1000 connections)
  - Scale up to S2 (5000 connections) if needed
  - 📖 [Azure deployment](azure/README.md)

## 🌐 Networking

### Private Endpoints

- [ ] **Verify private endpoints are created**
  - Cosmos DB: .36 (global), .37 (regional)
  - Redis: .38
  - SignalR: .39 (if using)
  - App Service: .40
  - 📖 [Azure deployment](azure/README.md)

- [ ] **Verify DNS resolution**
  - Test from App Service console:
    ```bash
    nslookup your-cosmos-account.documents.azure.com
    # Should resolve to 10.0.0.36 (private IP)
    ```
  - 📖 [Configuration guide](../getting-started/configuration.md#troubleshooting)

### Network Security Groups

- [ ] **Verify NSG rules**
  - App Service subnet: Allow outbound to private endpoint subnet
  - Private endpoint subnet: Allow inbound from App Service subnet
  - 📖 [Azure deployment](azure/README.md)

## 🧪 Testing

### Pre-Deployment Testing

- [ ] **Run all tests locally**
  ```bash
  dotnet test src/Chat.sln
  # Verify: all tests passing
  ```

- [ ] **Build in Release mode**
  ```bash
  dotnet build ./src/Chat.sln -c Release
  ```

- [ ] **Test with production-like configuration**
  - Use staging environment with production SKUs
  - Load test with expected user count
  - 📖 [Testing guide](../development/testing.md)

### Post-Deployment Verification

- [ ] **Smoke test after deployment**
  - [ ] Login flow works (OTP generation and verification)
  - [ ] Can join a room
  - [ ] Can send messages
  - [ ] Messages appear in real-time for other users
  - [ ] Read receipts update correctly
  - [ ] Language switching works
  - [ ] Health checks return 200 OK

- [ ] **Verify metrics in Application Insights**
  - [ ] See incoming requests
  - [ ] See custom metrics (chat.otp.requests, chat.messages.sent)
  - [ ] No errors in logs
  - [ ] Dependencies (Cosmos, Redis) show successful calls

## 📋 Final Checklist

Before go-live:

- [ ] All secrets configured (pepper, connection strings)
- [ ] Security headers verified (HSTS, CSP)
- [ ] Private endpoints working (no public access)
- [ ] Health checks passing
- [ ] Monitoring configured (alerts, dashboards)
- [ ] Rate limiting tested
- [ ] Backup/disaster recovery plan documented
- [ ] Incident response plan ready
- [ ] Team trained on monitoring dashboards
- [ ] Rollback plan prepared

## 🚨 Emergency Contacts

Document these before go-live:

- [ ] **Primary on-call**: [Name, contact]
- [ ] **Secondary on-call**: [Name, contact]
- [ ] **Azure support**: [Support plan tier]
- [ ] **Incident escalation path**: [Process]

## 📖 Additional Resources

- [Azure App Service Best Practices](https://learn.microsoft.com/azure/app-service/app-service-best-practices)
- [Cosmos DB Performance Tips](https://learn.microsoft.com/azure/cosmos-db/performance-tips)
- [Azure Security Baseline](https://learn.microsoft.com/security/benchmark/azure/)
- [SignalR Chat Operations Guide](../operations/monitoring.md)

---

**Last updated**: November 2025 | [Edit this checklist](https://github.com/smereczynski/SignalR-Chat/edit/main/docs/deployment/production-checklist.md)

**Next**: [Deployment Guide](azure/) | [Back to docs](../README.md)
