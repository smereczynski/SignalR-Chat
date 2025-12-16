## 1. Server Logging

### How logs are emitted
- Primary logger: Serilog is configured early in Program.cs with a bootstrap pipeline. Console output (stdout only; errors to stderr) is controlled by `Serilog__WriteToConsole`.
- Minimum levels:
  - Global: Information
  - Microsoft.* overridden to Warning (reduces framework noise).
- Enrichment: Environment name, machine name, thread id.
- HTTP access logging: `UseSerilogRequestLogging()` in `Startup.Configure()` adds structured per-request logs (enriched with a `TraceId` if the custom tracing middleware sets `X-Trace-Id`).
- OpenTelemetry logging provider: Added via `services.AddLogging(lb => lb.AddOpenTelemetry(...))`. This duplicates log events through an OpenTelemetry logger provider (still Serilog remains authoritative for structure).

### Export / sinks
- Console sink only (no file sink configured—so not written to disk by default).
- OpenTelemetry log exporter selection (in `Startup`):
  1. If Application Insights connection string present AND environment is Production: Azure Monitor (logs flow to Application Insights).
  2. Else if `OTel:OtlpEndpoint` configured: OTLP exporter (gRPC/HTTP to collector).
  3. Else: Console exporter (JSON-ish or human-friendly depending on provider defaults).

### Where they physically end up
- When no exporters are configured, logs can go to console (stdout/stderr) if enabled.
- Production with AI connection string: logs flow to Application Insights (query via Kusto / Log Analytics).
- There is no file rotation / disk persistence currently—ephemeral console only.

Canonical reference:
- See **[Configuration Guide](../getting-started/configuration.md#logging-configuration)** for default console behavior in Azure and how to temporarily enable stdout.

### Best‑practice consumption
- Local: run app and use `grep`, or better, pipe container logs into `jq` if you later add a JSON sink.
- Cloud: query Application Insights (if configured) with Kusto (e.g., `traces | where customDimensions.TraceId == '...'`).
- Correlation: Use the `TraceId` from request logs to jump into distributed traces (Activities exported via OTel exporters).

## 2. Health & Metrics

### Endpoints
1. Liveness/basic readiness: `GET /healthz` → returns `"ok"` (very lightweight; does not probe dependencies).
2. Metrics snapshot (side-channel): `GET /healthz/metrics`
   - Returns JSON with: `uptimeSeconds`, `activeConnections`, `messagesSent`, `roomsJoined`, `otpRequests`, `otpVerifications`, `reconnectAttempts`, `timestamp`.
   - Backed by `InProcessMetrics.Snapshot()` (pure in-memory counters).
3. Authenticated chat presence: `GET /api/health/chat/presence` (requires auth)
   - Returns per-room user presence (room name, users (username/device), counts).

### In-process metrics model
- `InProcessMetrics` holds atomic counters (Interlocked-backed).
- For each increment, it also updates an OpenTelemetry `Meter`:
  - Counters: `chat.messages.sent`, `chat.rooms.joined`, `chat.otp.requests`, `chat.otp.verifications`, `chat.reconnect.attempts`
  - UpDownCounters: `chat.connections.active`, `chat.room.presence` (with room label)
  - Availability events counter: `chat.user.availability.events` with dimensions (user, state, device)

### OTel export path
- A unified OpenTelemetry `services.AddOpenTelemetry()` pipeline configures:
  - Traces: AspNetCore, HttpClient + custom sources (your `Tracing.ActivitySource`).
  - Metrics: Runtime instrumentation, AspNetCore, HttpClient + the Meter(s).
  - Logs: Optional as above.
- Exporter chosen per resource type (trace/metrics/log) using the same priority logic (Azure Monitor > OTLP > Console).
- No Prometheus scrape endpoint is currently exposed; metrics leave the process through exporters—not via `/metrics`.

### Best‑practice consumption
- For lightweight dashboards or readiness checks: poll `/healthz/metrics`.
- For production-grade observability:
  - Use Application Insights Metrics Explorer if AI exporter active.
  - Or attach an OTLP collector (e.g., OpenTelemetry Collector + forward to Prometheus / Grafana / Loki / Tempo).
- For presence monitoring (Ops or admin dashboard): call `api/health/chat/presence` (can cache 1–5s client-side).

### Gaps / potential improvements
- Add a proper readiness endpoint that verifies downstream dependencies (DB/Redis).
- Add Prometheus exporter or /metrics endpoint if you want to adopt Prometheus stack directly.
- Rate-limit or auth protect `/healthz/metrics` if exposed publicly (currently unauthenticated).

## 3. Telemetry (Client → Server → Exporters)

### Client emission model
- Helper: `postTelemetry(event, data)` in chat.js.
- Suppression: Maintains `_telemetryRecent` map keyed by event family (e.g., `send.flush.skip|reason`) with TTL (4s) to avoid high-chatter spam.
- Payload fields:
  - `evt` (event name, e.g., `hub.connect.retry`, `send.queue`, `messages.page.ok`, `auth.hub.start.early`, `net.offline`, etc.)
  - `ts` timestamp (ms)
  - `sessionId` (lightweight random correlated id)
  - Arbitrary `data` (attempt counts, reasons, sizes, durations, categories)
- Transport: POST JSON to `/api/telemetry/reconnect` (even for non-reconnect events currently; naming mismatch but still accepted).

### Server ingestion
- `TelemetryController.Reconnect()` receives model `ReconnectAttemptDto(int Attempt, int DelayMs, string? ErrorCategory, string? ErrorMessage)`.
  - NOTE: Current mismatch: client sends generic fields like `evt`, `sessionId`, not the strongly typed DTO (only reconnect semantics are mapped). Other event types the client posts will send extra fields ignored by model binder.
  - The controller wraps the attempt in an Activity: `Client.ReconnectAttempt`.
  - Tags: attempt number, delay, optional error category/message (truncated).
  - Increments custom metric `_metrics.IncReconnectAttempt()` (which pushes to `chat.reconnect.attempts` counter).
  - Returns HTTP 202 Accepted; no persistence.

### Export / storage
- Activities (spans) export via the configured trace exporter.
- Metrics increment flows into OTel exporters.
- No server-side disk persistence or queue for client telemetry—it is fire-and-forget:
  - If exporter/back-end down: spans may be dropped (depending on exporter buffering).
  - If network from client fails: the `fetch` error is ignored silently.

### Best‑practice immediate reading
- Real-time local development: use console exporter (already fallback) to see spans and metrics printed.
- Production:
  - If AI: use Application Insights “traces” / “dependencies” / “customEvents” (depends on mapping) to view `Client.ReconnectAttempt` spans.
  - If OTLP: query via back-end (Jaeger/Tempo for traces; Prometheus for metrics if you route them; or directly in OpenTelemetry Collector with another exporter).
- For debugging noisy skip logic: temporarily raise telemetry TTL or log to console inside `postTelemetry`.

### Recommendations / Hardening
| Area | Current State | Suggested Enhancement |
|------|---------------|-----------------------|
| Client → Server telemetry schema | Overloads reconnect endpoint for all events | Add `/api/telemetry/events` with a generic DTO and event-type routing |
| Loss handling | Fire-and-forget, no retry | Add small retry (exponential up to 2 attempts) for critical events |
| Backpressure | TTL suppression only | Introduce batching (enqueue & POST array) to reduce request count |
| Observability coupling | Only reconnect attempt becomes Activity | Map other events (queue flush, auth grace) into spans or metrics for richer analysis |
| Governance | No PII scrubbing layer (rely on client discipline) | Add server-side whitelist or key filtering before tagging spans |

## 4. Health vs Telemetry vs Logging Summary

| Concern | Mechanism | Persisted? | Endpoint? | Exported? | Best Quick Check |
|---------|-----------|-----------|-----------|-----------|------------------|
| Liveness | `/healthz` | No | Yes (string) | No | Curl returns ok |
| Point metrics | `/healthz/metrics` | In-memory only | Yes (JSON) | Some also exported as OTel | Curl & jq parse |
| Presence | `/api/health/chat/presence` (auth) | In-memory hub state | Yes (JSON) | No | Authenticated GET |
| Client reconnect attempts | Activity + counter | In-memory → exporter | Indirect (POST ingest) | Yes (traces + metric) | Query traces / metrics |
| General client events (queue, offline) | Currently posted to same reconnect endpoint but mostly ignored server-side | Not persisted (except reconnect metric) | Same ingest | Only reconnect metric/span | Enhance controller |
| Server logs | Serilog + OTel logs | Console (stdout; errors to stderr) if enabled | No | Possibly AI/OTLP | Tail container logs |
| Server traces | System.Diagnostics.Activity + instrumentation | Memory buffer → exporter | No | Yes (AI/OTLP/Console) | Trace backend query |
| Server metrics | OTel Meters + runtime | Memory → exporter | No (except snapshot endpoint) | Yes | Metrics backend graph |

## 5. Easiest Aligned Consumption Paths

### Local development
- Logs: Terminal (stdout/stderr) if enabled; or export via OTLP / Application Insights if configured.
- Traces & Metrics: Set `OTel:OtlpEndpoint=http://localhost:4317`, run an OpenTelemetry Collector + Jaeger/Prometheus.
- Quick state: `curl http://localhost:5099/healthz/metrics | jq`.

### Production (with Application Insights)
1. Ensure `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
2. Use:
   - Metrics Explorer for `chat.messages.sent`, `chat.connections.active`.
   - Logs (Kusto) to correlate by `TraceId`.
   - Transaction search / traces for reconnect spans.

### Lightweight Ops Dashboard
- Poll `/healthz/metrics` every 5–10s (non-auth).
- Poll `/api/health/chat/presence` behind auth (cache 2–3s) for room occupancy.

## 6. Concrete Short-Term Improvements (Optional Roadmap)

1. Introduce a generic telemetry ingestion endpoint:
   - POST `/api/telemetry/events` with `{ evt, ts, sessionId, attrs:{} }`.
   - Map selected `evt` prefixes into metrics counters (e.g., `send.queue`, `auth.grace.*`).
2. Add retry & batch on client:
   - Maintain small ring buffer; POST array every N seconds or size threshold.
3. Add `/readyz` endpoint:
   - Checks DB/Redis connectivity + hub readiness before returning 200.
4. Add structured JSON sink (Serilog `WriteTo.Console(new CompactJsonFormatter())`) for easier downstream parsing.
5. Export Prometheus metrics (optional) via `OpenTelemetry.Exporter.Prometheus.AspNetCore`.

## 7. Quick Commands (Local Inspection)

```bash
# Health snapshot
curl -s http://localhost:5099/healthz/metrics | jq

# Presence (requires auth cookie; example uses curl with stored cookie jar)
curl -s --cookie cookies.txt http://localhost:5099/api/health/chat/presence | jq

# Plain liveness
curl -s -o /dev/null -w \"%{http_code}\\n\" http://localhost:5099/healthz
```

(Adjust port to match your run args.)

---

## 8. Executive TL;DR
- Logs: Structured, console only by default; exported to AI/OTLP if configured; not written to disk.
- Health: Basic `ok` liveness + richer `/healthz/metrics` JSON; presence via authenticated endpoint.
- Telemetry: Client fire-and-forget POSTs (rate-limited locally) to a reconnect-focused endpoint; only reconnect attempts fully transformed into spans + metrics; others currently underutilized server-side.
- Metrics & Traces: Emitted through OpenTelemetry with dynamic exporter selection; accessible in AI or any OTLP-compatible backend.
- Easiest consumption: `curl /healthz/metrics`, tail logs, query traces in chosen backend.