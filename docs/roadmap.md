# Roadmap

Phase breakdown for TraceCapsule. All seven phases below have a real, tested
implementation — this file now doubles as a map of what lives where. See the root
[README.md](../README.md) for the full product rationale (problem, architecture, capsule
format, privacy model, etc).

## Phase 1 — HTTP capture (`TraceCapsule.Core`, `TraceCapsule.AspNetCore`, `TraceCapsule.OpenTelemetry`) ✅

- `TraceCapsuleMiddleware`: buffers the request, captures the response, times the request,
  records exceptions, and persists a capsule per `CapturePolicy`
- `CapsuleActivityListener`: bridges the .NET `Activity` graph into the capsule's trace section
- `RedactionEngine` + `RedactionPolicy`: header/JSON-field redaction (mask / remove / keep_last_4)
- `CapsuleWriter` / `CapsuleReader`: the ZIP layout (`metadata.json`, `request.json`, ...)
- Tested in `UnitTests` (redaction, writer/reader round-trip) and `IntegrationTests`
  (`TraceCapsuleMiddlewareTests`, against a real hosted `SimpleApi`)

## Phase 2 — CLI (`TraceCapsule.Cli`) ✅

- `tracecapsule inspect <file>.capsule` — summary + Phase 7 analysis findings
- `tracecapsule replay <file>.capsule --target-url <url> [--latency dep=ms]` — re-issues the
  recorded request against a live target, writes a new result capsule
- `tracecapsule compare <production> <replay>` — status/exception/per-span diff
- `tracecapsule export --trace <id> --from <dir>` — resolves a trace id to its capsule(s)
- `tracecapsule merge <session-id> --from <dir>` — Phase 5 combine
- Tested in `ReplayTests` (`CapsuleReplayerTests` against a real loopback HTTP server,
  `CapsuleComparerTests`)

## Phase 3 — External API recording (`TraceCapsule.Http`) ✅

- `RecordingHttpMessageHandler`: records outbound `HttpClient` calls (including timeouts/
  connection failures, as a call with no response) and forwards the session id + active
  fault-injection instructions onto the request
- `ReplayHttpMessageHandler`: serves recorded responses instead of hitting the network,
  consulting `FaultInjectionContext` for Phase 6
- Tested in `UnitTests`

## Phase 4 — Message queue replay (`TraceCapsule.RabbitMQ`) ✅

- `QueueEventRecorder`: pure recording logic (no RabbitMQ.Client dependency)
- `RecordingChannelExtensions` / `RecordingConsumerHandler`: thin RabbitMQ.Client v7 adapters
- `QueueEmulator`: replays recorded consume events, in order, with no broker required
- Tested in `UnitTests`, including against a real `BasicDeliverEventArgs`/`BasicProperties`

## Phase 5 — Distributed replay ✅

- `CapsuleMerger`: combines per-service partial capsules sharing a session id (or trace id)
- `samples/DistributedTransferDemo`: three real ASP.NET Core services (Transfer, Fraud,
  Payment) on three loopback ports, with an intentional Payment-timeout bug baked in
- Tested in `UnitTests` (`CapsuleMergerTests`) and `IntegrationTests`
  (`DistributedTransferDemoTests`, which runs all three services for real)

## Phase 6 — Fault injection / chaos replay ✅

- `FaultInjectionOptions`/`FaultInjectionContext`: ambient fault instructions, threaded
  through the CLI's `--latency` flag, the `X-TraceCapsule-Fault-Latency` header, and
  consulted by both `ReplayHttpMessageHandler` and `QueueEmulator`
- Tested in `UnitTests` and `ReplayTests`

## Phase 7 — Diagnostic AI agent (optional) ✅

- `HeuristicIncidentAnalyzer`: a dependency-free, rule-based `IIncidentAnalyzer` — names the
  failing span, the slowest/failing external call, and any published-but-never-consumed
  message, wired into `tracecapsule inspect`'s output
- Strictly an analysis layer on top of recording/replay/comparison — a real LLM-backed
  `IIncidentAnalyzer` can be swapped in without touching anything else
- Tested in `UnitTests`

## Advanced Features implemented ahead of schedule

- **Deterministic replay** (`ITraceCapsuleClock` / `ITraceCapsuleIdGenerator` /
  `ITraceCapsuleRandom` in `TraceCapsule.Core.Determinism`): the exact abstraction the
  README's "Deterministic Replay Problem" section describes. `RecordingClock` /
  `RecordingIdGenerator` / `RecordingRandom` capture every value returned during recording
  (`Capsule.Determinism`, a `determinism.json` capsule section) with a single sequence
  counter shared across all three kinds; `ReplayClock` / `ReplayIdGenerator` / `ReplayRandom`
  hand the identical values back in the same order, throwing a clear
  `TraceCapsuleDeterminismException` if replay asks for more than was recorded (i.e. the code
  path diverged). `services.AddTraceCapsuleDeterminismReplay(capsule)` flips a host into
  replay mode. Wired into `DistributedTransferDemo`'s payment-service (it generates its
  payment id and timestamp through these instead of `Guid.NewGuid()`/`DateTime.UtcNow`) and
  proven end to end in `DeterminismReplayTests`: the real returned payment id/timestamp are
  what the capsule recorded, and a fresh replay instance reproduces them exactly.

- **HTML capsule report** (`tracecapsule inspect <path> --html <output>`,
  `TraceCapsule.Cli.Report.HtmlReportBuilder`): a self-contained, shareable HTML page — no
  external stylesheets/scripts/fonts — rendering the header/status, metrics, Phase 7 analysis
  findings, exceptions with stack traces, a span waterfall, external-call and queue-event
  tables, and the (already-redacted) request/response bodies. Every value pulled from the
  capsule is HTML-encoded before being written out, since capsule content originates from a
  production request and has to be treated as untrusted when rendered as markup. Verified
  visually against real capsules from `SimpleApi` (a screenshot of an exception capsule and
  of a redacted request both render correctly), in addition to `HtmlReportBuilderTests`
  (including a dedicated XSS-encoding test).

- **Redaction policy from a config file** (`TraceCapsule.Core.Redaction.RedactionPolicyLoader`):
  loads a `RedactionPolicy` from the exact YAML shape the README shows
  (`redaction: { headers, json_fields, strategies }`, via YamlDotNet) or from a plain JSON
  file (a direct `RedactionPolicy` serialization). `samples/SimpleApi` now loads its policy
  from `redaction.yaml` instead of hardcoding it in `Program.cs`. Tested in
  `RedactionPolicyLoaderTests`, including the README's own YAML example verified to redact
  exactly as documented.

## What's genuinely out of scope (by design, not oversight)

- **Database/Redis state snapshot + replay, time travel.** Mentioned in the README's
  "Advanced Features" as future work; none of the current phases depend on them, and they'd
  need their own recording/replay abstraction analogous to determinism's.
- **A real LLM-backed analyzer.** `HeuristicIncidentAnalyzer` is the reference
  implementation; plugging in a model is a follow-up, not a blocker.
- **Kafka support.** The architecture (recorder → ambient context → emulator) is
  broker-agnostic, but only RabbitMQ has an adapter today.
