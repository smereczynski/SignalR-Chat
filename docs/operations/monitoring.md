# Monitoring & Observability

This document describes the telemetry, logging, and metrics that are **currently configured in code** and where the data lands:

- **Azure Monitor / Application Insights** (and its connected Log Analytics workspace)
- **OTLP collector** (when Application Insights is not configured)
- **Console / file logs** (local, or when configured)

It also documents the app’s built-in health/diagnostics endpoints.

---
## 1. High-level architecture
The app emits telemetry through two pipelines:

1) **OpenTelemetry SDK** (registered in `Startup.ConfigureServices`)
- Traces, metrics, and *optional* log export
- Exporter auto-selection (Azure Monitor / OTLP / Console)

2) **Serilog** (bootstrapped in `Program.cs`)
- Structured application logs and HTTP request logs
- Sinks: Console (always Error+ to stderr), optional File, optional Application Insights, optional OTLP

Important notes:
- In `Testing__InMemory=true` mode, external dependencies are not registered; Redis tracing instrumentation is skipped.
- You can enable **both** Serilog + OpenTelemetry exporters to the same backend; this can create duplicate log entries. If you see duplication, prefer one log pipeline for your backend.

---
## 2. Export destinations (what goes where)

### 2.1 Application Insights / Log Analytics
When `APPLICATIONINSIGHTS_CONNECTION_STRING` is present:

- **OpenTelemetry logs exporter** (`AddAzureMonitorLogExporter`) sends application logs to Application Insights.
- **OpenTelemetry metrics exporter** (`AddAzureMonitorMetricExporter`) sends metrics to Application Insights.
- **OpenTelemetry trace exporter** (`AddAzureMonitorTraceExporter`) is enabled **only when** `ASPNETCORE_ENVIRONMENT=Production`.
- **Serilog Application Insights sink** sends Serilog events as Application Insights “traces”.

Where to query the data:
- In the **Application Insights UI**, you’ll primarily use the *Logs* blade.
- In the connected **Log Analytics workspace**, you’ll typically query these tables:
	- `AppTraces` (log/traces)
	- `AppRequests` (incoming requests)
	- `AppDependencies` (outgoing HTTP/Redis dependencies)
	- `AppExceptions` (exceptions)
	- `AppMetrics` (custom metrics / measurements)

Exact table availability can depend on how the Application Insights resource is configured (workspace-based vs classic) and which exporters are active.

### 2.2 OTLP collector
When `OTel__OtlpEndpoint` is configured and Application Insights exporters are not selected:

- OpenTelemetry traces/metrics/logs export via OTLP (gRPC by default, HTTP/Protobuf when the endpoint includes port `4318`).
- Serilog can also export logs via its OTLP sink using the same `OTel__OtlpEndpoint` env var.

The final “where” depends on your collector backend:
- LGTM-style stacks usually map OTLP traces → Tempo, logs → Loki, metrics → Prometheus/Mimir.

### 2.3 Console output
If neither Application Insights nor OTLP is configured:

- OpenTelemetry exporters fall back to Console exporters.
- Serilog always writes **Error+** logs to stderr. When `Serilog:WriteToConsole=true`, Serilog also writes lower-severity logs to stdout.

### 2.4 File logs
File logging is opt-in:

- Enable with `Serilog__WriteToFile=true`.
- Output path: `src/Chat.Web/logs/chat-<date>.log` (rolling daily, retains 7 files).

This is local disk logging; on Azure App Service it is not a durable store unless you mount persistent storage.

---
## 3. Configuration switches (as implemented)

### 3.1 Key environment variables
- `APPLICATIONINSIGHTS_CONNECTION_STRING`
	- Enables Serilog → Application Insights sink.
	- Enables OpenTelemetry logs + metrics exporters to Azure Monitor.
	- Enables OpenTelemetry trace exporter **only** when `ASPNETCORE_ENVIRONMENT=Production`.

- `OTel__OtlpEndpoint`
	- Enables Serilog → OTLP sink.
	- Enables OpenTelemetry traces/metrics/logs exporters to OTLP (when Azure Monitor isn’t selected).
	- If the endpoint contains `:4318`, OTLP uses HTTP/Protobuf; otherwise it defaults to gRPC.

- `Serilog__WriteToConsole=true|false`
	- Controls whether Serilog emits non-error logs to stdout.
	- Regardless of this setting, Serilog still emits **Error+** logs to stderr.

- `Serilog__WriteToFile=true|false`
	- Enables rolling file logs at `src/Chat.Web/logs/chat-<date>.log`.

### 3.2 appsettings.json notes
`appsettings.Development.json` and `appsettings.Production.json` set baseline log levels and default `Serilog:WriteToConsole` values.

Note: The `ApplicationInsights:SamplingSettings` section exists in appsettings, but the app does **not** register the classic `AddApplicationInsightsTelemetry()` SDK. Sampling behavior is therefore driven by the OpenTelemetry tracer sampler (see Tracing section).

---
## 4. Tracing

### 4.1 Automatic instrumentation
Configured in `Startup.ConfigureServices`:
- ASP.NET Core server instrumentation
- HttpClient instrumentation
- Redis instrumentation (only when not `Testing__InMemory=true`)

### 4.2 Manual spans (ActivitySource = `Chat.Web`)
The application defines `Tracing.ActivitySource` with source name `Chat.Web`.

Manual span names currently used:

- HTTP + API
	- `http.request` (created by `RequestTracingMiddleware`)
	- `api.messages.retry-translation`

- SignalR hub
	- `ChatHub.OnConnected`, `ChatHub.OnDisconnected`
	- `ChatHub.Join`, `ChatHub.Leave`
	- `ChatHub.SendMessage`, `ChatHub.MarkRead`
	- `ChatHub.GetUsers`, `ChatHub.GetHealthStatus`, `ChatHub.Heartbeat`
	- `ChatHub.RetryTranslation`

- Persistence (Cosmos)
	- `cosmos.users.*` (`get`, `getall`, `getbyupn`, `getid`, `upsert`)
	- `cosmos.rooms.*` (`getall`, `getbyid`, `getbyname`)
	- `cosmos.messages.*` (`create`, `delete`, `getbyid`, `recent`, `before`, `markread`, `updatetranslation`)

- Translation pipeline
	- `translation.queue.enqueue`, `translation.queue.dequeue`, `translation.queue.requeue`, `translation.queue.remove`
	- `translation.job.process`

- Client-emitted telemetry
	- `Client.ReconnectAttempt` (from `POST /api/telemetry/reconnect`)

### 4.3 Trace headers / correlation
`RequestTracingMiddleware` adds the following response headers for non-static requests:
- `X-Trace-Id`
- `X-Span-Id`

Serilog request logging (`UseSerilogRequestLogging`) enriches request logs with `TraceId` when the middleware has set `X-Trace-Id`.

### 4.4 Sampling
OpenTelemetry tracing uses `TraceIdRatioBasedSampler(0.2)` (20% sampling). If you need full-fidelity traces during an incident, increase the sampler rate in `Startup.cs`.

---
## 5. Metrics

### 5.1 Custom domain metrics
Created via `System.Diagnostics.Metrics.Meter` in `Chat.Web`:

- Counters
	- `chat.messages.sent`
	- `chat.rooms.joined`
	- `chat.otp.requests`
	- `chat.otp.verifications`
	- `chat.otp.verifications.ratelimited`
	- `chat.reconnect.attempts` (includes tags `attempt` and `delay_ms`)
	- `chat.user.availability.events` (includes tags `user` and `state`)
	- `chat.markread.ratelimit.violations` (includes tag `user`)

- UpDownCounters
	- `chat.connections.active`
	- `chat.room.presence` (includes tag `room`)

### 5.2 Automatic metrics
Enabled via OpenTelemetry:
- .NET runtime metrics (`AddRuntimeInstrumentation`)
- ASP.NET Core metrics
- HttpClient metrics

### 5.3 In-process snapshot endpoint
`GET /healthz/metrics` returns a simple JSON snapshot (side-channel) from `IInProcessMetrics`:
- uptime
- active connections
- message/room/OTP/reconnect counters

This endpoint does not replace OpenTelemetry exports; it’s meant for quick checks and simple dashboards.

### 5.4 Dashboards
Grafana dashboards (see `grafana/dashboards/*.json`):
- `chat-dev-overview.json` – high-level service health
- `chat-dev-traces.json` – latency & trace error rates
- `chat-dev-logs.json` – structured log panels

Import JSON into Grafana → add data source (Azure Monitor or OTLP Prometheus) → adjust panel queries to match environment labels.

---
## 6. Logging

### 6.1 What is logged
Logging is structured and primarily uses Serilog (plus standard `ILogger<T>` usage throughout the app). Key logging sources:
- HTTP access logs via `UseSerilogRequestLogging`
- Global exception logs via `GlobalExceptionHandlerMiddleware`
- Dependency logs (Cosmos/Redis/Azure SDKs), controlled by per-namespace log level overrides
- Health publishing logs via `ApplicationInsightsHealthCheckPublisher`

Security note: user-controlled input is sanitized using `LogSanitizer` before being written to logs.

### 6.2 Where logs go

Serilog sinks (configured in `Program.cs`):
- Console: always Error+ to stderr; lower levels are controlled by `Serilog:WriteToConsole` / `Serilog__WriteToConsole`.
- File: opt-in via `Serilog__WriteToFile=true`.
- Application Insights: enabled when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- OTLP: enabled when `OTel__OtlpEndpoint` is set.

OpenTelemetry log exporter (configured in `Startup.cs`):
- Azure Monitor logs exporter when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- Otherwise OTLP when `OTel__OtlpEndpoint` is set.
- Otherwise Console exporter.

Azure App Service note:
- When Serilog writes to stdout/stderr, Azure App Service can forward those streams into Log Analytics (commonly as `AppServiceConsoleLogs`). In production `appsettings.Production.json` defaults to `Serilog:WriteToConsole=false` to reduce volume.

---
## 7. Health & diagnostics endpoints

Endpoints:
- `/healthz` – liveness
- `/healthz/ready` – readiness (adds Redis/Cosmos/ACS-config checks when running with real dependencies)
- `/healthz/metrics` – lightweight metrics snapshot
- `POST /api/telemetry/reconnect` – client-emitted reconnect telemetry (creates a span and increments metrics)

### 7.1 Verifying Readiness
```bash
curl -s -o /dev/null -w '%{http_code}\n' https://<app-host>/healthz/ready
```
Expect `200` when all dependencies healthy.

---
## 8. Recommended queries

### 8.1 Application Insights (Log Analytics) examples

Recent errors:
```kusto
AppTraces
| where SeverityLevel >= 3
| order by TimeGenerated desc
| take 200
```

Slow requests:
```kusto
AppRequests
| where DurationMs > 1000
| order by TimeGenerated desc
| take 200
```

Dependency failures:
```kusto
AppDependencies
| where Success == false
| order by TimeGenerated desc
| take 200
```

### 8.2 OTLP backends
Query patterns depend on your backend (Tempo/Loki/Prometheus, etc). Use the span names and metric names listed above as the primary lookup keys.

---
## 9. Production guidance
| Area | Recommendation |
|------|----------------|
| Sampling | Traces are sampled at 20% by default; adjust for incidents. |
| Retention | Retention is configured in the destination (Application Insights / Log Analytics / your OTLP backend). |
| Cardinality | Avoid embedding high-cardinality user identifiers as metric labels; use logs/traces instead. |
| Sensitive Data | Never log OTP codes, secrets; sanitization already strips control chars. |
| Scaling | If trace volume grows, deploy dedicated OTLP collector with batching/persistent storage enabled. |

---
## 10. Troubleshooting
| Symptom | Cause | Action |
|---------|-------|--------|
| No trace headers | Static asset or middleware executed after response started | Verify middleware order; `RequestTracingMiddleware` must be early. |
| Missing Redis spans | Running with `Testing__InMemory=true` | Disable test mode for full instrumentation. |
| High ingestion cost | Excess debug logs in production | Raise minimum level or remove verbose categories. |
| Empty Grafana panels | Data source mismatch | Check datasource (Azure Monitor vs OTLP backend) and metric/span names. |
| AI traces missing in non-prod | By design | OpenTelemetry trace exporter to Azure Monitor is enabled only when `ASPNETCORE_ENVIRONMENT=Production`. |
| Duplicate logs in AI | Both Serilog + OTel exporting logs | Prefer one log pipeline for the backend (disable one exporter/sink). |

---
## 11. Extending telemetry
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
## 12. Verification checklist (post-deploy)
- [ ] `/healthz/ready` returns 200
- [ ] Application Insights traces present (prod)
- [ ] Custom counters visible in Metrics explorer / Grafana
- [ ] Log query shows structured properties (`TraceId`, `SourceContext`)
- [ ] Redis & Cosmos dependency spans present (non-test)
- [ ] Response headers include `X-Trace-Id` / `X-Span-Id`

---
## 13. References
- `src/Chat.Web/Program.cs`
- `src/Chat.Web/Startup.cs`
- `src/Chat.Web/Observability/RequestTracingMiddleware.cs`
- `src/Chat.Web/Middleware/GlobalExceptionHandlerMiddleware.cs`
- `src/Chat.Web/Controllers/HealthMetricsController.cs`
- `src/Chat.Web/Controllers/TelemetryController.cs`
- `src/Chat.Web/Services/InProcessMetrics.cs`
- `grafana/dashboards/*.json`

---
**Last Updated**: 2025-12-17
