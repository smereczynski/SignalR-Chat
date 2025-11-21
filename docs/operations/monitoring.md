# Monitoring & Observability

**Status**: P1 (Created Nov 21, 2025)
**Applies To**: Production, Staging, Dev (reduced retention in dev)

This document explains how the SignalR Chat application collects, exports and analyzes telemetry: **Traces**, **Metrics**, **Logs**, and **Health**. It also covers **Azure Monitor / Application Insights**, **OTLP collectors**, and using **Grafana dashboards**.

---
## 1. Overview
The observability stack follows a layered export priority:
1. **Azure Monitor Exporters** (when `APPLICATIONINSIGHTS_CONNECTION_STRING` present and environment = Production)
2. **OTLP Exporters** (when `OTel__OtlpEndpoint` is configured)
3. **Console Exporters** (fallback for local development)

Serilog provides structured application logs. OpenTelemetry providers add distributed tracing, metrics, and optional log export.

Telemetry is intentionally disabled for external dependencies in in-memory test mode (no Redis / Cosmos spans) to keep test runs fast.

---
## 2. Architecture
Key components:
- `Program.cs` – Bootstrap logging (Serilog sinks: Console, optional File, Application Insights, OpenTelemetry sink)
- `Startup.ConfigureServices` – Registers OpenTelemetry (Traces + Metrics + Logs) after dependencies.
- `RequestTracingMiddleware` – Creates an Activity per non-static HTTP request; adds `X-Trace-Id`/`X-Span-Id` response headers.
- `Tracing.ActivitySource` – Custom source name `Chat.Web` used across manual spans.

Exporter selection helpers (three overloads of `AddSelectedExporter`) choose **AzureMonitor > OTLP > Console** for traces, metrics, and logs.

---
## 3. Environment Variables & Configuration
| Purpose | Key | Example |
|---------|-----|---------|
| App Insights Connection | `APPLICATIONINSIGHTS_CONNECTION_STRING` | `InstrumentationKey=...;IngestionEndpoint=...` |
| OTLP Endpoint | `OTel__OtlpEndpoint` | `http://localhost:4317` or `http://collector:4318` |
| File Logging Enable | `Serilog__WriteToFile` | `true` |
| ASP.NET Environment | `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` |
| In-Memory Test Mode | `Testing__InMemory` | `true` (skips Redis/Cosmos instrumentation) |

Notes:
- Port `4318` implies HTTP/Protobuf; any other port defaults to gRPC for OTLP.
- In production, if Application Insights is configured it overrides OTLP exporter for traces/metrics/logs.

---
## 4. Tracing
### 4.1 Automatic Instrumentation
Enabled providers (when not disabled by test mode):
- ASP.NET Core (`AddAspNetCoreInstrumentation`)
- HttpClient (`AddHttpClientInstrumentation`)
- Redis (`AddRedisInstrumentation`) – only when real Redis is configured (not `Testing__InMemory=true`).

### 4.2 Manual Spans
`RequestTracingMiddleware` starts an Activity named `http.request` with tags:
- `http.method`
- `http.path`
- `http.status_code`

Additional spans come from automatic instrumentations (e.g. outgoing HTTP calls, Redis dependency spans).

### 4.3 Trace Propagation
- Standard W3C Trace Context headers are emitted.
- For every non-static request `X-Trace-Id` and `X-Span-Id` are added to the response (before headers are sent) enabling quick correlation.

### 4.4 Verification
```bash
# Run locally (console exporters)
Testing__InMemory=true dotnet run --project src/Chat.Web --urls=https://localhost:5099

# Issue a request and inspect headers
curl -k -I https://localhost:5099/ | grep -E 'X-Trace-Id|X-Span-Id'
```
Expect both headers unless the request was a static asset.

---
## 5. Metrics
### 5.1 Custom Domain Metrics
Defined in `Startup` nested static class `Metrics`:
- `chat.messages.sent` – Increment when a message is sent.
- `chat.rooms.joined` – User joins room.
- `chat.otp.requests` – OTP request endpoint invoked.
- `chat.otp.verifications` – OTP verification attempts.
- `chat.reconnect.attempts` – Client reconnection events.

### 5.2 Automatic Metrics
Runtime instrumentation: CPU, GC, threadpool (via `AddRuntimeInstrumentation`).
ASP.NET Core and HttpClient metrics (request duration, active requests, outgoing HTTP dependency metrics).

### 5.3 Export & Retention
Retention handled by Log Analytics workspace (30 / 90 / 365 days for dev / staging / prod). Metrics exported via:
- Azure Monitor Metric exporter (Production with App Insights connection)
- OTLP (Collector) otherwise
- Console fallback (Development with no exporters configured)

### 5.4 Dashboards
Grafana dashboards (see `grafana/dashboards/*.json`):
- `chat-dev-overview.json` – high-level service health
- `chat-dev-traces.json` – latency & trace error rates
- `chat-dev-logs.json` – structured log panels

Import JSON into Grafana → add data source (Azure Monitor or OTLP Prometheus) → adjust panel queries to match environment labels.

---
## 6. Logging
### 6.1 Serilog Configuration
Bootstrap in `Program.cs`:
- Console sink (always)
- Optional file sink (`logs/chat-<date>.log`) – enable with `Serilog__WriteToFile=true`
- Application Insights sink (when connection string present)
- OpenTelemetry sink (when OTLP endpoint configured) – forwards logs as OTLP spans/events

Log enrichment:
- `EnvironmentName`, `MachineName`, `ThreadId`
- Correlation via `TraceId` when Serilog request logging runs (added in `EnrichDiagnosticContext`).

### 6.2 Log Levels
Overrides in logger configuration:
- `Microsoft` – Info/Warning depending on environment
- `Microsoft.Azure.Cosmos` – Information (always capture DB operations)
- `StackExchange.Redis` – Information
- Reduce noise in production by elevating non-critical framework sources to Warning.

### 6.3 Sanitization
All user-controlled values pass through `LogSanitizer` (see middleware and OTP store) to prevent log injection (CWE-117). Never remove sanitizer calls.

### 6.4 Verification
```bash
# Local run with file logging
Serilog__WriteToFile=true Testing__InMemory=true dotnet run --project src/Chat.Web
ls -1 src/Chat.Web/logs/
```

---
## 7. Health Checks
Endpoints:
- `/healthz` – Liveness (always returns self check)
- `/healthz/ready` – Readiness (includes Redis/Cosmos/ACS checks when in cloud mode)

Redis & Cosmos registered only when not in-memory mode. Publisher pushes health statuses to Application Insights every 30s (see `ApplicationInsightsHealthCheckPublisher`).

### 7.1 Verifying Readiness
```bash
curl -s -o /dev/null -w '%{http_code}\n' https://<app-host>/healthz/ready
```
Expect `200` when all dependencies healthy.

---
## 8. Production Guidance
| Area | Recommendation |
|------|----------------|
| Sampling | Use full sampling initially; introduce adaptive later if ingestion costs rise. |
| Retention | 365 days in prod (already configured), review quarterly. |
| Cardinality | Avoid embedding high-cardinality user identifiers as metric labels; use logs/traces instead. |
| Sensitive Data | Never log OTP codes, secrets; sanitization already strips control chars. |
| Scaling | If trace volume grows, deploy dedicated OTLP collector with batching/persistent storage enabled. |

---
## 9. Troubleshooting
| Symptom | Cause | Action |
|---------|-------|--------|
| No trace headers | Static asset or middleware executed after response started | Verify middleware order; `RequestTracingMiddleware` must be early. |
| Missing Redis spans | Running with `Testing__InMemory=true` | Disable test mode for full instrumentation. |
| High ingestion cost | Excess debug logs in production | Raise minimum level or remove verbose categories. |
| Empty Grafana panels | Data source mismatch | Check datasource (Azure Monitor vs Prometheus/OTLP) and metric names. |
| AI exporter silent | Connection string missing or env ≠ Production | Ensure `APPLICATIONINSIGHTS_CONNECTION_STRING` set and environment is `Production`. |

---
## 10. Extending Telemetry
### 10.1 Add Custom Trace
```csharp
using var span = Tracing.ActivitySource.StartActivity("messages.persist", ActivityKind.Internal);
span?.SetTag("room", roomName);
```

### 10.2 Add Counter
```csharp
// Add new counter in Startup.Metrics static class
public static readonly Counter<long> MessagesDeleted = Meter.CreateCounter<long>("chat.messages.deleted");
MessagesDeleted.Add(1, new("room", roomName));
```
Avoid high-cardinality labels (e.g. user IDs) – prefer room grouping.

### 10.3 Add Log Property
```csharp
_logger.LogInformation("User {User} joined room {Room}", LogSanitizer.Sanitize(user), room);
```

---
## 11. Verification Checklist (Post-Deploy)
- [ ] `/healthz/ready` returns 200
- [ ] Application Insights traces present (prod)
- [ ] Custom counters visible in Metrics explorer / Grafana
- [ ] Log query shows structured properties (`TraceId`, `SourceContext`)
- [ ] Redis & Cosmos dependency spans present (non-test)
- [ ] Response headers include `X-Trace-Id` / `X-Span-Id`

---
## 12. References
- `src/Chat.Web/Program.cs`
- `src/Chat.Web/Startup.cs`
- `src/Chat.Web/Observability/RequestTracingMiddleware.cs`
- `infra/bicep/modules/monitoring.bicep`
- `grafana/dashboards/*.json`

---
**Last Updated**: 2025-11-21
