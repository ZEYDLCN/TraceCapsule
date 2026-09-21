# Roadmap

Phase breakdown for TraceCapsule. Nothing beyond the repo skeleton (empty, buildable
projects wired together in `TraceCapsule.sln`) exists yet — this is the plan of record
for what each phase adds. See the root [README.md](../README.md) for the full product
rationale (problem, architecture, capsule format, privacy model, etc).

## Phase 1 — HTTP capture (`TraceCapsule.Core`, `TraceCapsule.AspNetCore`, `TraceCapsule.OpenTelemetry`)

- ASP.NET Core middleware that wraps a request
- OpenTelemetry trace/span capture
- HTTP request/response capture
- Redaction engine (headers + JSON fields)
- Capsule format + writer (ZIP: `metadata.json`, `request.json`, `trace.json`, ...)

## Phase 2 — CLI (`TraceCapsule.Cli`)

- `tracecapsule inspect <file>.capsule`
- `tracecapsule replay <file>.capsule`
- `tracecapsule compare <a>.capsule <b>.capsule`

## Phase 3 — External API recording (`TraceCapsule.Http`)

- `HttpClient` `DelegatingHandler` that records outbound calls
- Replay-time HTTP mock server that serves recorded responses instead of hitting the real dependency

## Phase 4 — Message queue replay (`TraceCapsule.RabbitMQ`)

- Producer/consumer event recording
- Correlation via `messageId` / `correlationId`
- Event replay against a queue emulator

## Phase 5 — Distributed replay

- Multiple services contributing to the same capsule
- `samples/DistributedTransferDemo` becomes a real multi-service banking workflow
  (Transfer API → Fraud API → RabbitMQ → Payment Worker → mock Banking API)

## Phase 6 — Fault injection / chaos replay

- `tracecapsule replay bug.capsule --latency payment-api=3000`
- Controlled failure injection on top of a recorded production scenario (retry, circuit
  breaker, fallback, idempotency checks)

## Phase 7 — Diagnostic AI agent (optional)

- Analyzes a capsule's trace/logs/exceptions/timing/events
- Suggests a likely root cause
- Strictly an analysis layer on top of recording/replay/comparison — never a
  requirement for the core system to function

## Current status

Only the repository skeleton exists: solution + empty class libraries / console app /
samples / test projects, all referencing each other per the dependency graph in the
README's "Repository Structure" section. `dotnet build` and `dotnet test` both succeed
with placeholder content. Phase 1 implementation starts in `TraceCapsule.Core`.
