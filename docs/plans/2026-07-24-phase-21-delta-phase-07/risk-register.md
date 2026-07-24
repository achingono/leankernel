# Phase 21 Delta + Phase 07 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | Learning pipeline blocks turn response if turn-event queue or subscriber is synchronous | Response latency increases, user-visible degradation | Bounded async channel + non-blocking enqueue; subscriber runs in separate process (`LeanKernel.Learning`) | Open |
| R2 | `LeanKernel.Services.Learning` circular dependency on `LeanKernel.Services.Gateway` | Project architecture violation | `LeanKernel.Services.Learning` references `LeanKernel.Core`, `LeanKernel.Logic`, `LeanKernel.Data`, `LeanKernel.Services.Common` — never `LeanKernel.Services.Gateway` | Open |
| R3 | Enrichment derived artifacts accidentally broaden availability scope | Cross-channel data leak | Enforce scope inheritance at `EnrichmentHostedService` output; code review scope preservation logic | Open |
| R4 | Dream cycle lock timeout causes false-positive skip on long-running GBrain operations | Lost Dream windows | Make `DreamLockTimeoutSeconds` configurable with generous default (300s); add `TimedOut` recovery path that retries with backoff | Open |
| R5 | Cron expression library unavailable or incorrect DST handling | Missed or double-fired jobs | Use `Cronos` NuGet package (battle-tested); unit tests for DST transitions and edge cases | Open |
| R6 | `LeanKernel.Services.Learning` service startup order and dependency on GBrain | Learning worker starts before GBrain is ready | Learning service only needs database and GBrain; no hard dependency on Gateway. On transient GBrain failure, Dream cycle and knowledge write-back retry with backoff | Open |
| R7 | Enrichment queue grows unbounded if enrichment worker falls behind ingestion | Memory/resource pressure | Bounded queue capacity configuration; `QueueCapacity` default of 100; consumer-producer monitoring | Open |
| R8 | In-memory Dream cycle lock lost on service restart | Concurrent Dream runs possible after restart | Stale lock is bounded by `DreamLockTimeoutSeconds`; restart resets in-memory locks; GBrain side has its own idempotency | Open |
| R9 | Turn-event queue recovery misses events if `IEventStore` is pruned | Learning pipeline misses turns | Set event retention policy longer than typical recovery window; log gap warnings | Open |
| R10 | `FactExtractionService` keyed `IChatClient` registration duplicated across Gateway and Learning | Configuration drift | Create shared `AddFactExtractionChatClient` helper in `LeanKernel.Services.Common.Extensions` | Open |

## Open Decisions
- Should `LeanKernel.Services.Learning` use a message queue (RabbitMQ/NATS) instead of in-memory channel for turn events? **Decision: Start with in-memory channel for simplicity; DB-backed event store as durability fallback.**
- What Cronos library version to use? **Decision: `Cronos` v0.8+ for .NET 8 compatibility.**
- Should Dream run reports use the existing event spine or a dedicated table? **Decision: Dedicated `DreamRunRecord` table for structured query; event spine for audit trail.**
