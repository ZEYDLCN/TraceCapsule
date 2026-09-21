# TraceCapsule

## Install the beta

Requires .NET 8 or later. Install the ASP.NET Core integration in your application:

```bash
dotnet add package TraceCapsule.AspNetCore --version 0.1.0-beta.1
```

Optional integrations are available as `TraceCapsule.Http`, `TraceCapsule.OpenTelemetry`,
and `TraceCapsule.RabbitMQ`, using the same version. Install the CLI separately:

```bash
dotnet tool install --global TraceCapsule.Cli --version 0.1.0-beta.1
tracecapsule --help
```

This is an early beta. Live RabbitMQ broker integration has not been validated, and merged
distributed capsules do not yet preserve determinism events. Redaction covers configured
JSON fields and headers; it does not guarantee removal of secrets from arbitrary text,
query strings, exceptions, or span tags. See [the test report](https://github.com/ZEYDLCN/TraceCapsule/blob/master/docs/test-report.md)
for the tested scenarios and remaining limitations.

Maintainers: [automatic NuGet publishing](docs/publishing.md) uses version tags and GitHub
Actions with NuGet Trusted Publishing.

**TraceCapsule**, production ortamında oluşan hataları sanitize edilmiş, taşınabilir ve yeniden oynatılabilir bir `.capsule` dosyasına dönüştüren bir **debugging ve replay SDK/library** projesidir.

Amaç, production'da oluşan bir bug'ı sadece loglardan incelemek yerine mümkün olduğunca aynı execution context ile local veya test ortamında yeniden üretmektir.

> **Don't debug production from screenshots. Replay it.**

---

## Problem

Production'da distributed bir sistem hata verdiğinde developer genellikle şu kaynaklar arasında dolaşır:

```text
Grafana
  ↓
Application Logs
  ↓
OpenTelemetry Trace
  ↓
API Gateway Logs
  ↓
Database Queries
  ↓
RabbitMQ Events
  ↓
External API Responses
```

Örneğin kullanıcı şu işlemi gerçekleştiriyor:

```
POST /api/transfers
```

Akış:

```
Transfer API
  ↓
Customer Service
  ↓
Fraud Service
  ↓
RabbitMQ
  ↓
Payment Service
  ↓
External Banking API
  ↓
Database
```

Production'da hata oluşuyor.

Developer bug'ı local ortamda yeniden üretmek istediğinde aşağıdakilerin **hepsine aynı anda** sahip olması gerekir, ama genelde sahip değildir:

- aynı request body
- aynı headers
- aynı external API response
- aynı database state
- aynı event sequence
- aynı configuration
- aynı timing

**Sonuç:** Production'da görülen bug localde tekrar oluşmaz.

Bu problem özellikle şu alanlarda daha da zor hale gelir:

- distributed systems
- microservices
- asynchronous messaging
- external API integrations
- race conditions
- environment-specific bugs

## Solution

TraceCapsule production execution sırasında gerekli debugging bilgisini otomatik toplar.

```
Production Request
       ↓
TraceCapsule Middleware
       ↓
Execution Collector
       ↓
┌────────────────────────────┐
│ HTTP Request / Response    │
│ Distributed Trace          │
│ External API Calls         │
│ Queue Events                │
│ DB Query Fingerprints       │
│ Application Logs            │
│ Config Fingerprint          │
│ Timing Information          │
└────────────────────────────┘
       ↓
PII / Secret Redaction
       ↓
Serialization
       ↓
bug-1842.capsule
```

Developer daha sonra:

```bash
tracecapsule replay bug-1842.capsule
```

komutunu çalıştırır. TraceCapsule şu akışı başlatır:

```
Capsule Load
  ↓
Environment Setup
  ↓
External Dependencies Mock
  ↓
Request Replay
  ↓
Event Replay
  ↓
Execution Comparison
```

### Core Idea

TraceCapsule'ın temel sorusu:

> "Production'da tam olarak ne oldu ve bunu local ortamda tekrar nasıl çalıştırabilirim?"

## Example Scenario

Production'da kullanıcı para transferi yapıyor.

```
POST /api/transfers
```

Request:

```json
{
  "fromAccount": "ACC-102",
  "toAccount": "ACC-550",
  "amount": 5000
}
```

Execution:

```
Transfer API
  ↓
Fraud API
  ↓
Balance API
  ↓
RabbitMQ
  ↓
Payment Consumer
  ↓
Database
```

Production'da `500 Internal Server Error` oluşuyor.

TraceCapsule otomatik olarak şunları toplar:

- request
- headers
- trace
- external calls
- message events
- timings
- exceptions
- config fingerprint

Oluşturulan dosya: `transfer-error-1842.capsule`

Developer:

```bash
tracecapsule inspect transfer-error-1842.capsule
```

Output:

```
Trace ID:      7f92bd...
Request:       POST /api/transfers
Duration:      1321 ms
Services:      TransferService, FraudService, PaymentService
Failed Span:   PaymentService.ReserveBalance
Exception:     TimeoutException
External API:  Balance API
Recorded Response: HTTP 504
```

Sonra:

```bash
tracecapsule replay transfer-error-1842.capsule
```

## Replay Architecture

```
.capsule
   ↓
Capsule Loader
   ↓
Replay Orchestrator
   ↓
┌────────────────────┐
│ HTTP Mock Server    │
│ Queue Emulator      │
│ Config Provider     │
│ Time Provider       │
│ State Loader        │
└────────────────────┘
   ↓
Application
   ↓
Replay Trace
   ↓
Comparison Engine
```

## Capsule Format

Örnek bir capsule metadata yapısı:

```json
{
  "traceId": "7f92bd12",
  "timestamp": "2026-09-21T10:14:33Z",

  "request": {
    "method": "POST",
    "path": "/api/transfers"
  },

  "services": [
    "transfer-service",
    "fraud-service",
    "payment-service"
  ],

  "environment": {
    "appVersion": "2.3.1",
    "configFingerprint": "9ac21..."
  }
}
```

Capsule içinde ayrı bölümler olabilir:

```
metadata.json
request.json
trace.json
external-http.json
events.json
exceptions.json
timing.json
config.json
```

## Privacy & Security

Production verisinin doğrudan developer'ın bilgisayarına taşınması güvenli değildir. Bu yüzden TraceCapsule'ın en önemli bileşenlerinden biri **Redaction Engine** olmalıdır.

Örneğin production response:

```json
{
  "name": "Zeyd",
  "tcNumber": "12345678901",
  "cardNumber": "1111222233334444"
}
```

Capsule:

```json
{
  "name": "Z***",
  "tcNumber": "***",
  "cardNumber": "****4444"
}
```

Policy:

```yaml
redaction:
  headers:
    - Authorization
    - Cookie

  json_fields:
    - password
    - token
    - tcNumber
    - cardNumber

  strategies:
    cardNumber: keep_last_4
    tcNumber: remove
```

> **Implemented:** Bu tam olarak yukarıdaki YAML'ı gerçekten yükleyen bir kod var —
> `RedactionPolicyLoader.FromFile("redaction.yaml")` (YamlDotNet ile) veya bir JSON dosyasından
> (`RedactionPolicy`'nin doğrudan serileştirilmiş hâli). `samples/SimpleApi` politikayı
> `Program.cs` içine gömmek yerine `redaction.yaml`'dan yüklüyor.

## Deterministic Replay Problem

Production replay'in en zor taraflarından biri execution'ın deterministik olmamasıdır. Örneğin uygulama `DateTime.UtcNow`, `Guid.NewGuid()`, `Random.Next()` kullanıyor olabilir.

Production: Time `12:00:01`, Generated ID `abc-123`
Replay: Time `14:32:52`, Generated ID `xyz-991`

olursa davranış değişebilir. TraceCapsule bu değerleri abstraction üzerinden kontrol edebilir. Örneğin:

```csharp
public interface ISystemClock
{
    DateTime UtcNow { get; }
}
```

Replay sırasında `RecordedClock` kullanılır. Aynı yaklaşım `Time`, `Random`, `UUID`, `External API` için uygulanabilir.

> **Implemented:** `TraceCapsule.Core.Determinism` gerçekten bu üç abstraction'ı sağlıyor —
> `ITraceCapsuleClock`, `ITraceCapsuleIdGenerator`, `ITraceCapsuleRandom`. Uygulama kodu
> `DateTime.UtcNow`/`Guid.NewGuid()`/`Random.Next()` yerine bunları enjekte eder;
> `RecordingClock`/`RecordingIdGenerator`/`RecordingRandom` üretilen her değeri (tek, üç türe
> paylaşılan bir sequence sayacıyla) `Capsule.Determinism`'e kaydeder, `ReplayClock`/
> `ReplayIdGenerator`/`ReplayRandom` aynı değerleri aynı sırayla geri verir — kayıttan fazlası
> istenirse (kod yolu ayrıştıysa) net bir `TraceCapsuleDeterminismException` fırlatır.
> `services.AddTraceCapsuleDeterminismReplay(capsule)` bir host'u replay moduna geçirir.
> `samples/DistributedTransferDemo`'nun payment-service'i bunu gerçekten kullanıyor —
> `DeterminismReplayTests` gerçek bir çağrının ürettiği payment id/timestamp'in capsule'e
> kaydedildiğini ve replay'in aynı değerleri ürettiğini kanıtlıyor.

## External API Recording

Production execution:

```
Application
  ↓
Fraud API
```

Fraud API:

```json
{ "riskScore": 81 }
```

TraceCapsule bu response'u kaydeder. Replay sırasında gerçek Fraud API çağrılmaz:

```
Application
  ↓
TraceCapsule Mock
  ↓
Recorded Response
```

Böylece external service availability, network state, API version gibi değişkenler replay'i bozmaz.

## Message Queue Replay

TraceCapsule ilerleyen versiyonlarda RabbitMQ / Kafka gibi sistemleri destekleyebilir.

Production event:

```json
{
  "eventType": "TransferRequested",
  "messageId": "msg-102",
  "correlationId": "corr-551"
}
```

Capsule içinde `events.json` dosyasına kaydedilir. Replay şu akışı yeniden oluşturabilir:

```
TransferRequested
  ↓
Consumer
  ↓
TransferValidated
  ↓
PaymentRequested
```

## OpenTelemetry Integration

TraceCapsule'ın temel telemetry kaynağı OpenTelemetry olabilir. Sistem Trace, Span, Events, Attributes, Exceptions bilgilerini kullanır. Örneğin:

```
Trace ID: 7f92bd
  Span: POST /transfer
  Span: FraudService.Check
  Span: PaymentService.ReserveBalance
  Span: Database.Commit
```

## Correlation

Distributed sistemlerde aynı request farklı servislerden geçer:

```
API Gateway
  ↓
Transfer Service
  ↓
Fraud Service
  ↓
Payment Service
```

TraceCapsule `traceId`, `correlationId`, `causationId`, `messageId` bilgilerini kullanarak execution chain oluşturabilir.

## CLI

```bash
tracecapsule inspect error.capsule
```

Output:

```
TraceCapsule
Trace:           7f92bd
Duration:        1.32s
Services:        3
External Calls:  4
Events:          6
Exceptions:      1
```

Replay:

```bash
tracecapsule replay error.capsule
```

Diff:

```bash
tracecapsule compare error.capsule replay-result.capsule
```

Output:

```
Production vs Replay

PaymentService.ReserveBalance
  Production: Timeout after 2000ms
  Replay:     Timeout after 2000ms
  MATCH
```

**HTML report:** `tracecapsule inspect error.capsule --html report.html` writes a
self-contained, shareable HTML page — status, metrics, analysis findings, exceptions with
stack traces, a span waterfall, external-call/queue-event tables, and the (already-redacted)
request/response bodies — instead of only ever reading text in a terminal.

**Response body diffing:** `tracecapsule compare` also diffs the response body structurally
(field by field, not byte by byte), so fields that are expected to vary between runs don't
show up as noise:

```bash
tracecapsule compare error.capsule replay-result.capsule --ignore-body-field '$.createdAt'
```

`--ignore-body-field` is repeatable and accepts `*` as a wildcard segment (e.g.
`'$.items[*].updatedAt'` to ignore that field on every array element).

**Regression test generation:** once a fix has been verified locally (replay the bug capsule
against the fixed target, saving the result), turn both capsules into a self-contained xUnit
test that proves the bug stays fixed:

```bash
tracecapsule replay error.capsule --target-url http://localhost:5000 --output fixed.capsule
tracecapsule generate-test error.capsule fixed.capsule --output BugRegressionTest.cs \
  --ignore-body-field '$.transferId' --ignore-body-field '$.completedAt'
```

The generated test has no dependency on this CLI or on any capsule file at run time — the
recorded request, the original failure signature, and the now-expected response are baked in
as literals. It reads its target's base URL from the `TRACECAPSULE_TARGET_URL` environment
variable (override the variable name with `--target-url-env`), so a CI job just needs to
start the target before `dotnet test` runs. If the target replays external dependencies too,
wire them up with one call from `TraceCapsule.Http`:

```csharp
services.AddTraceCapsuleReplayEnvironment(capsule);
```

which registers a named `HttpClient` for every dependency the capsule recorded, each serving
recorded responses instead of hitting the network.

## Developer Integration

ASP.NET Core:

```csharp
builder.Services.AddTraceCapsule(options =>
{
    options.EnableHttpRecording = true;
    options.EnableOpenTelemetry = true;
    options.EnableRedaction = true;
});

app.UseTraceCapsule();
```

`TraceCapsule` attribute belirli endpoint'lerde aktif edilebilir:

```csharp
[TraceCapsule]
[HttpPost("/transfer")]
public async Task<IActionResult> Transfer(...)
{
}
```

## Sampling

Her production request'in kaydedilmesi pahalı olabilir. Bu yüzden bir sampling policy kullanılabilir:

- Normal request → %0.1 sample
- Error request → %100 capture
- High latency request → capture
- Specific customer incident → capture

```yaml
capture:
  errors: true
  latency_threshold_ms: 1500
  sampling_rate: 0.001
```

## Trigger Conditions

Capsule otomatik oluşturulabilir:

- HTTP 5xx
- Exception
- Timeout
- High latency
- Manual trigger
- Specific trace ID

## Architecture

```
                    APPLICATION
                         │
                         ▼
               TraceCapsule Middleware
                         │
        ┌────────────────┼────────────────┐
        │                │                │
        ▼                ▼                ▼
   HTTP Recorder    Trace Collector   Event Recorder
        │                │                │
        └────────────────┼────────────────┘
                         │
                         ▼
                  Redaction Engine
                         │
                         ▼
                  Capsule Builder
                         │
                         ▼
                   .capsule File
                         │
                         ▼
                Replay Environment
                         │
        ┌────────────────┼────────────────┐
        ▼                ▼                ▼
     HTTP Mock       Event Replay       Config Replay
                         │
                         ▼
                 Execution Comparison
```

## MVP

İlk versiyonda bütün distributed system replay problemini çözmeye çalışma.

**MVP 1** — Sadece ASP.NET Core HTTP request replay: HTTP Request + HTTP Response + OpenTelemetry Trace + Exception + Redaction → `.capsule` output.

**MVP 2** — External HTTP dependency recording: `HttpClient` → TraceCapsule Handler → External Response Recording. Replay sırasında recorded HTTP responses mock olarak kullanılır.

**MVP 3** — CLI: `inspect`, `replay`, `compare` komutları.

**MVP 4** — RabbitMQ support: Producer, Consumer, Message, Correlation, Replay.

**MVP 5** — Distributed service support: birden fazla servis aynı capsule içinde (API Gateway, Transfer Service, Fraud Service, Payment Service).

## Proposed Tech Stack

- **Core SDK:** .NET 8 / C#
- **Observability:** OpenTelemetry
- **Storage:** ilk MVP'de Local Files, ileri versiyonda S3 / MinIO, PostgreSQL
- **Serialization:** JSON, MessagePack
- **CLI:** .NET CLI
- **Local Infrastructure:** Docker, Docker Compose

Capsule formatı ZIP tabanlı olabilir — `incident-102.capsule` aslında:

```
ZIP
├── metadata.json
├── trace.json
├── request.json
├── external.json
└── events.json
```

## Advanced Features

İleri seviyede eklenebilecek özellikler:

- Database state snapshot
- Queue replay
- Redis state capture
- Configuration replay
- Time travel
- Deterministic randomness
- Fault injection

### Fault Injection

Bir capsule başarıyla replay edildikten sonra:

```bash
tracecapsule replay bug.capsule --latency payment-api=3000
```

çalıştırılabilir. Bu, **Recorded Production Scenario + Controlled Failure** ile resiliency testing yapılmasını sağlar. Örneğin Payment API'ye +3000ms latency eklendikten sonra:

- Retry çalıştı mı?
- Circuit breaker açıldı mı?
- Fallback devreye girdi mi?
- Duplicate transaction oluştu mu?

ölçülebilir. Bu noktada TraceCapsule **Production Replay + Chaos Testing** platformuna dönüşür.

## AI Integration

TraceCapsule'ın çalışması için AI gerekli değildir. Ancak optional bir **Incident Analysis Agent** eklenebilir. Agent capsule içeriğini (Trace, Logs, Exceptions, Timing, Events) analiz eder ve örneğin şu türde bir öneri verir:

```
Likely root cause:
PaymentService timeout caused RabbitMQ consumer retry.

Possible issue:
Downstream Balance API latency increased from 120ms to 2.4s.
```

AI yalnızca analiz katmanıdır. Asıl sistem recording, replay ve comparison üzerine kuruludur.

## Why Not Just Logs?

- **Logs:** "Payment failed" der.
- **Trace:** hangi servislerden geçti, gösterir.
- **TraceCapsule:** aynı execution'ı tekrar çalıştırmayı hedefler.

### Difference From Standard Observability

Observability "What happened?" sorusuna cevap verir. TraceCapsule "Can I reproduce what happened?" sorusuna cevap vermeye çalışır.

### Difference From Error Monitoring Tools

Error monitoring sistemi Exception, Stack Trace, Request Metadata gösterebilir. TraceCapsule'ın odağı **Portable, Sanitized, Replayable Execution Artifact** oluşturmaktır.

## Core Engineering Challenges

Projenin asıl mühendislik değeri şu problemleri çözmek olacaktır:

- Distributed tracing
- Deterministic replay
- PII redaction
- Dependency mocking
- Event replay
- State reconstruction
- Correlation
- Serialization
- Sampling
- Performance overhead

## Performance Requirement

TraceCapsule production uygulamasını ciddi şekilde yavaşlatmamalıdır. Bu yüzden recording mümkün olduğunca Async, Buffered, Sampled, Non-blocking olmalıdır:

```
Application Thread
  ↓
In-memory Buffer
  ↓
Background Worker
  ↓
Capsule Storage
```

## Project Roadmap

See [docs/roadmap.md](docs/roadmap.md) for the full phase breakdown (Phase 1 → Phase 7).

## Example User Experience

Developer production'da hata görüyor. Grafana'dan Trace ID `7f92bd` alıyor.

```bash
tracecapsule export --trace 7f92bd
```

Output: `Capsule created: transfer-timeout.capsule`

```bash
tracecapsule inspect transfer-timeout.capsule
tracecapsule replay transfer-timeout.capsule
```

Developer production bug'ını local ortamda tekrar çalıştırabiliyor.

## Possible Packages

NuGet:

- `TraceCapsule.Core`
- `TraceCapsule.AspNetCore`
- `TraceCapsule.OpenTelemetry`
- `TraceCapsule.Http`
- `TraceCapsule.RabbitMQ`
- `TraceCapsule.Redis`

CLI: `TraceCapsule.Cli`

## Repository Structure

```
TraceCapsule/
  src/
    TraceCapsule.Core/
    TraceCapsule.AspNetCore/
    TraceCapsule.OpenTelemetry/
    TraceCapsule.Http/
    TraceCapsule.Cli/
    TraceCapsule.RabbitMQ/

  samples/
    SimpleApi/
    DistributedTransferDemo/

  tests/
    UnitTests/
    IntegrationTests/
    ReplayTests/

  docs/
```

This matches the current state of the repo — the solution (`TraceCapsule.sln`) wires all of the above together, and every phase in [docs/roadmap.md](docs/roadmap.md) has a real, tested implementation: capsule recording/redaction/writing (`Core`), the ASP.NET Core middleware (`AspNetCore`), span capture (`OpenTelemetry`), outbound HTTP recording + replay + fault injection (`Http`), RabbitMQ event recording + the queue emulator (`RabbitMQ`), and the `inspect`/`replay`/`compare`/`export`/`merge` CLI (`Cli`). `SimpleApi` and `DistributedTransferDemo` are runnable, not placeholders.

## Demo Application

`samples/DistributedTransferDemo` is a real, runnable banking workflow — three ASP.NET Core
services (Transfer, Fraud, Payment), each independently wired with TraceCapsule, hosted on
three loopback ports in one process:

```
Client
  ↓
Transfer API  →  Fraud API
  ↓
Payment API
```

```bash
dotnet run --project samples/DistributedTransferDemo
```

A normal transfer (`"amount": 100`) completes cleanly. A transfer of `"amount": 5000` or more
hits an intentional bug: Payment's handler sleeps 5 seconds while Transfer's HttpClient only
allows 2, reproducing a real timeout without any manual fault injection — exactly the
`PaymentService.ReserveBalance` / `TimeoutException` scenario from this README's own
motivating example. Each service writes its own partial capsule (they share a trace id via
standard W3C trace-context propagation, and a session id via the `X-TraceCapsule-Session`
header as a fallback for hops that don't propagate it), and:

```bash
tracecapsule merge <session-id> --from <capsules-dir>
```

combines the three into one complete artifact — `tracecapsule inspect` on it shows all three
services, the failing `payment-api` external call (recorded even though it timed out, with
no response), and the exception, all in one place.

## Pitch

**One-liner:** TraceCapsule is a developer SDK that turns production failures into sanitized, portable and replayable execution artifacts.

**Short pitch:** Production bug'larını debug ederken developer'ların sadece logs ve traces üzerinden tahmin yürütmesi yerine, TraceCapsule ilgili execution context'i güvenli bir `.capsule` dosyasına kaydeder ve aynı request, dependency responses ve event flow'un local ortamda replay edilmesini sağlar.

**CV description:**

> TraceCapsule — Production Incident Replay SDK. Designed a .NET developer SDK for capturing production incidents as sanitized and replayable execution artifacts. Integrated OpenTelemetry-based distributed tracing, HTTP dependency recording, PII redaction, correlation tracking and local request replay. Designed the architecture to support external service mocking, message queue replay and deterministic debugging of distributed systems.

## Why This Project Matters

TraceCapsule klasik CRUD App / AI Chatbot / RAG Demo / Dashboard projelerinden farklıdır.

- **Problem:** Production bug'larının yeniden üretilememesi.
- **Çözüm:** Production execution'ını güvenli ve replayable bir debugging artifact'ına dönüştürmek.

Bu yüzden proje Backend Engineering, Distributed Systems, Observability, Developer Tooling, Reliability Engineering, Security ve DevOps alanlarının kesişimindedir.

## Core Philosophy

> Production'da ne olduğunu görmek yeterli değildir.
> Mümkünse tekrar çalıştırabilmelisin.
