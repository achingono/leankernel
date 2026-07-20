# Phase 07 Deep Review Findings

Contextual and architectural review performed on 2026-07-20 against the current worktree for Phase 07 learning scheduler implementation in:

- `src/Services/LeanKernel.Services.Learning/`
- `src/Services/LeanKernel.Services.Common/`
- `src/Services/LeanKernel.Gateway/Providers/LearningPublishingChatHistoryProvider.cs`
- `src/Common/LeanKernel.Core/Entities/ScheduledJobEntity.cs`
- `src/Common/LeanKernel.Data/EntityContext.cs`
- `src/Common/LeanKernel.Data/Migrations/20260720022555_AddScheduledJobs.cs`
- `test/LeanKernel.Tests.Unit/Scheduler/`

This review follows `.agents/prompts/deep-review.prompt.md` and intentionally excludes pure static-analysis concerns (style, generic smells, CVEs, and syntax checks).

Severity scale used here:

- **Critical**: high-probability data integrity, security, or production availability impact.
- **Major**: correctness issue with clear user/data impact but narrower blast radius.
- **Suggestion**: hardening and maintainability improvements.

---

## Critical

### C1 — `LearningPublishingChatHistoryProvider` swallows the caller's cancellation token

- **File/Module:** `src/Services/LeanKernel.Gateway/Providers/LearningPublishingChatHistoryProvider.cs:53`
- **The Issue:** The `StoreChatHistoryAsync` override receives a `cancellationToken` parameter but passes `CancellationToken.None` when publishing the turn event:
  ```csharp
  await _eventPublisher.PublishAsync(turnEvent, CancellationToken.None).ConfigureAwait(false);
  ```
  If the inbound HTTP request is cancelled (client disconnect, gateway timeout), the learning-event POST continues indefinitely (bound only by the `HttpClient` timeout). In high-traffic scenarios this can accumulate in-flight requests and exhaust the connection pool.
- **Why Static Analysis Missed It:** The parameter exists on the method signature, so analyzers see it is "available". The defect is the explicit override with a different value.
- **Impact:** Connection-pool starvation under sustained client disconnection patterns; delayed gateway shutdown during rolling deployments.
- **Recommended Fix:** Pass the `cancellationToken` from the enclosing scope:
  ```csharp
  await _eventPublisher.PublishAsync(turnEvent, cancellationToken).ConfigureAwait(false);
  ```

### C2 — GBrain memory-client types relocated to `LeanKernel.Services.Common` but retained `LeanKernel.Gateway.*` namespaces

- **File/Module:** `src/Services/LeanKernel.Services.Common/Memory/GBrainAuthHandler.cs:8`, `src/Services/LeanKernel.Services.Common/Memory/GBrainMcpClient.cs:7`, `src/Services/LeanKernel.Services.Common/Memory/GBrainMemoryClient.cs:9`, `src/Services/LeanKernel.Services.Common/Memory/GBrainException.cs:5`, `src/Services/LeanKernel.Services.Common/Configuration/GBrainSettings.cs:5`
- **The Issue:** All five files were deleted from `src/Services/LeanKernel.Gateway/Memory/` and recreated in `LeanKernel.Services.Common/Memory/` and `LeanKernel.Services.Common/Configuration/`, but retain their original `LeanKernel.Gateway.*` namespaces. The `LeanKernel.Services.Learning` project references both namespaces via `using LeanKernel.Gateway.Memory;` and `using LeanKernel.Gateway.Configuration;` — which resolves because they are still in the `LeanKernel.Gateway` namespace. However, this creates a cross-project namespace leak: types in `LeanKernel.Services.Common` masquerade as belonging to `LeanKernel.Gateway`, which will confuse future developers and can cause assembly-load ambiguity if the gateway ever re-exports these types.
- **Why Static Analysis Missed It:** The code compiles and runs without error. The using-directives resolve correctly. No tool warns about a mismatch between project folder hierarchy and declared namespace by default.
- **Impact:** Maintenance confusion; risk of `CS0436` type-forward ambiguity if both projects later define types with the same name under `LeanKernel.Gateway.*`.
- **Recommended Fix:** Rename namespaces to match the actual project:
  - `LeanKernel.Gateway.Memory` → `LeanKernel.Services.Common.Memory`
  - `LeanKernel.Gateway.Configuration` → `LeanKernel.Services.Common.Configuration`
  - Update `using` directives in `Program.cs` of the Learning service and the `using LeanKernel.Gateway.Configuration` in `GBrainAuthHandler.cs`.

Also remove the unused `using LeanKernel.Entities;` import in `GBrainMemoryClient.cs` — no types from that namespace are referenced.

---

## Major

### M1 — `DbScheduledJobDefinitionProvider.GetEnabledJobsAsync` returns all jobs across all tenants with no filter

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Scheduler/DbScheduledJobDefinitionProvider.cs:23-29`
- **The Issue:** `GetEnabledJobsAsync` performs an unfiltered query that returns **every** enabled job in the `ScheduledJobs` table, regardless of tenant or channel. The `SchedulerHostedService` then evaluates and executes all of them. In a multi-tenant deployment, this leaks job definitions across tenants — Tenant A's scheduler can execute Tenant B's jobs.
- **Why Static Analysis Missed It:** The query uses `AsNoTracking()` and `Where(static job => job.Enabled)`, which is type-safe and valid SQL. The scope leak is semantic: no tenant/channel parameter is accepted by the method signature.
- **Impact:** Cross-tenant job execution; Tenant B's payload data (including `CompletedTurnEvent` payloads with user message text) is deserialized and processed by the scheduler running under Tenant A's service context.
- **Recommended Fix:** Add a scoping parameter to `GetEnabledJobsAsync` or scope the scheduler to per-tenant/channel instances. At minimum, the scheduler should filter by known tenant context:
  ```csharp
  public async Task<IReadOnlyList<ScheduledJobDefinition>> GetEnabledJobsAsync(
      Guid? tenantId = null, Guid? channelId = null, CancellationToken cancellationToken = default)
  ```
  And the `SchedulerHostedService` should resolve the current tenant/channel before loading jobs.

### M2 — `OnboardingGapDetector` has false-positive "name" detection on generic "i am" usage

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Learning/OnboardingGapDetector.cs:37`
- **The Issue:**
  ```csharp
  if (!ContainsAny(normalized, ["my name is", "you can call me", "i am ", "i'm "]))
  ```
  The trailing-space suffix `"i am "` and `"i'm "` are intended to reduce false matches, but "I am going to the store" (containing "i am ") and "I'm planning a trip" (containing "i'm ") still trigger a pass, marking "name" as *not* a gap. If a user says "I'm looking for help with my homework", the directive builder will not ask for their name, even though none was provided.
- **Why Static Analysis Missed It:** String matching against natural-language patterns is inherently semantic. No static analyzer can determine whether "I am" is an identity statement or a generic verb phrase.
- **Impact:** The onboarding experience will skip name collection for users whose first message is a statement about their intent rather than their identity.
- **Recommended Fix:** Remove `"i am "` and `"i'm "` from the name-gap closure list, or replace with more specific patterns such as `"i am called"`, `"call me"`, `"i go by"`. At minimum, reduce false positives by requiring these patterns only in sentences where they introduce a proper noun.

### M3 — `FactExtractionLearningStep` uses naive sentence splitting with no domain awareness

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Learning/FactExtractionLearningStep.cs:30`
- **The Issue:**
  ```csharp
  var facts = candidateText
      .SelectMany(static text => text.Split(['.', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      .Where(static sentence => sentence.Length > 20)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .Take(5)
      .ToList();
  ```
  Splitting on `.` produces fragments from:
  - Abbreviations ("Dr. Smith lives in N.Y.C." → "Dr", " Smith lives in N", "Y", "C")
  - URLs ("Visit example.com for more" → "Visit example", "com for more")
  - Decimal numbers ("Score was 98.5%" → "Score was 98", "5%")
  - Email addresses ("Contact me at user@example.com" → "Contact me at user@example", "com")
  
  The 20-character floor also silently discards all shorter statements, including valid facts ("Lives in NYC" = 12 chars).
- **Why Static Analysis Missed It:** The splitting logic is syntactically valid. The issue is domain-specific: no awareness of abbreviations, URLs, or numeric separators.
- **Impact:** Extracted facts may be garbage fragments that pollute knowledge pages. True short facts are silently lost.
- **Recommended Fix:** Replace naive `.` splitting with a sentence-boundary detector (e.g., `StringSplitOptions` with regex `(?<=[.!?])\s+`). Consider using an AI-based sentence segmenter for production quality. Remove the 20-character floor or lower it to 10.

### M4 — `SchedulerHostedService` has no circuit breaker for persistent DB failures

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Scheduler/SchedulerHostedService.cs:46-52`
- **The Issue:** When `GetEnabledJobsAsync` throws (e.g., DB transient failure, connection lost), the `catch` block logs a warning and retries immediately after `PollIntervalSeconds`:
  ```csharp
  catch (Exception ex)
  {
      logger.LogWarning(ex, "Failed to load scheduled jobs from persistence.");
      await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken).ConfigureAwait(false);
      continue;
  }
  ```
  There is no backoff, no circuit breaker, and no limit on consecutive failures. If the database is unavailable for an extended period, the scheduler spins in a tight poll-loop generating warning log entries at the full poll rate.
- **Why Static Analysis Missed It:** The pattern is a standard retry loop. No analyzer detects the absence of exponential backoff.
- **Impact:** Log flooding during DB outage; unnecessary CPU consumption polling a known-failing operation.
- **Recommended Fix:** Add exponential backoff on consecutive failures and cap the total number of retries before entering a degraded state:
  ```csharp
  var consecutiveFailures = 0;
  const int maxConsecutiveFailures = 5;
  // ... inside catch:
  consecutiveFailures++;
  var delaySeconds = Math.Min(_options.PollIntervalSeconds * (1 << Math.Min(consecutiveFailures, 5)), 300);
  if (consecutiveFailures >= maxConsecutiveFailures)
  {
      logger.LogError("Scheduler persistence unreachable after {Count} attempts; entering degraded mode.", consecutiveFailures);
  }
  await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken).ConfigureAwait(false);
  ```
  Reset `consecutiveFailures = 0` on a successful load.

### M5 — `CompletedTurnEvent` parameterless constructor with `{ get; init; }` properties enables accidental post-deserialization mutation

- **File/Module:** `src/Services/LeanKernel.Services.Common/Contracts/CompletedTurnEvent.cs:36-38`
- **The Issue:** The type provides a parameterless constructor alongside the full-signature constructor. All properties use `{ get; init; }`, which *appears* immutable but can be mutated via `with` expressions anywhere the instance is accessible. More critically, the parameterless constructor is used by `System.Text.Json` deserialization (in `ExecuteLearningStepScheduledJobHandler`, `ReplayTurnScheduledJobHandler`, `OnboardingGapDetectionScheduledJobHandler`), producing fully mutable instances. After deserialization, callers can use `with` expressions to alter tenant/user/person/channel IDs, enabling privilege escalation if the deserialized event were re-processed by a different security context.
- **Why Static Analysis Missed It:** `{ get; init; }` is the standard recommended pattern for DTOs. The analyzer cannot trace whether a mutated instance is re-processed across trust boundaries.
- **Impact:** Low probability in current code (instances are consumed once and discarded), but creates a latent security risk for future code that reuses deserialized events.
- **Recommended Fix:** Seal the type, remove the parameterless constructor, and add a `JsonConstructor` attribute on the primary constructor:
  ```csharp
  [JsonConstructor]
  public CompletedTurnEvent(
      Guid tenantId, Guid userId, Guid personId, Guid channelId,
      string? sessionId, string turnId, DateTimeOffset recordedAt,
      IReadOnlyList<TurnMessage> requestMessages,
      IReadOnlyList<TurnMessage> responseMessages)
  ```

---

## Suggestions

### S1 — `CapabilityGapLearningStep` phrase list misses common variants

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Learning/CapabilityGapLearningStep.cs:15-21`
- **Observation:** The gap phrase list covers `"i can't"`, `"i cannot"`, `"i don't have access"`, `"i am unable"`, `"not available"` but misses `"can't"` (without "i"), `"unable to"`, `"doesn't support"`, `"not supported"`, `"no access"`, and `"permission denied"`.
- **Recommendation:** Expand the phrase list to cover more assistant capability-denial patterns. Consider maintaining the list in configuration rather than code for operator customization.

### S2 — `EngagementTrackingLearningStep` signal format is fragile for aggregation

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Learning/EngagementTrackingLearningStep.cs:24`
- **Observation:** The signal is a flat semicolon-delimited string embedded in a knowledge page. Downstream aggregation requires parsing:
  ```
  request_chars=123;response_chars=456;messages=2
  ```
- **Recommendation:** Consider writing engagement signals as structured JSON or to a dedicated telemetry table for efficient querying.

### S3 — `HostedServicesTests` uses `Task.Delay` for synchronization (flaky in CI)

- **File/Module:** `test/LeanKernel.Tests.Unit/Scheduler/HostedServicesTests.cs:36,57,90-91`
- **Observation:** After `StartAsync`, tests wait with `Task.Delay(100)` or `Task.Delay(1200)` before asserting. These delays are sensitive to CI resource contention and will produce intermittent failures.
- **Recommendation:** Replace `Task.Delay` with a polling wait loop (as `LearningAndSchedulerIntegrationTests` already does via `WaitUntilAsync`).

### S4 — No test coverage for `FactExtractionLearningStep` edge cases

- **File/Module:** `test/LeanKernel.Tests.Unit/Scheduler/LearningPipelineStepTests.cs:15-33`
- **Observation:** The existing test only covers the happy path (assistant response contains sentences). Missing coverage:
  - Empty assistant response
  - Response with URLs, abbreviations, decimals
  - Response with exactly 5 sentences (boundary test for `Take(5)`)
- **Recommendation:** Add theory tests for edge cases.

### S5 — No test coverage for `KnowledgePageUpdateCoordinator` error paths

- **File/Module:** `test/LeanKernel.Tests.Unit/Scheduler/LearningPersistenceAndPublishingTests.cs:21-39`
- **Observation:** The test only covers `WriteFactAsync`. `WriteIdentityIntentAsync`, `WriteCapabilityGapAsync`, and `WriteEngagementSignalAsync` are untested at the coordinator level (they have indirect coverage through integration tests).
- **Recommendation:** Add direct unit tests for the remaining three coordinator methods to ensure correct key prefix, content format, and scope propagation.

### S6 — `MemoryOnboardingDirectivePublisher` throws `ArgumentException` for null/empty directive, but upstream never produces null

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Learning/MemoryOnboardingDirectivePublisher.cs:17`
- **Observation:** The `ArgumentException.ThrowIfNullOrWhiteSpace(directive)` guard can never trigger in the current call chain because `OnboardingDirectiveBuilder.BuildDirective` always returns a non-null string. If the guard does trigger, it will surface as a `500` error in the onboarding gap handler rather than a graceful no-op.
- **Recommendation:** Replace the exception with a no-op log warning:
  ```csharp
  if (string.IsNullOrWhiteSpace(directive)) { logger.LogWarning("..."); return Task.CompletedTask; }
  ```

### S7 — `OnboardingGapDetectionScheduledJobHandler` logs `Information` for common no-gap result

- **File/Module:** `src/Services/LeanKernel.Services.Learning/Scheduler/OnboardingGapDetectionScheduledJobHandler.cs:55`
- **Observation:** The log message `"No onboarding gaps detected for scheduled job {JobName} turn {TurnId}."` is emitted at `Information` level. For a production deployment processing thousands of turns, this will be the majority case and will create log noise.
- **Recommendation:** Downgrade to `Debug` level.

### S8 — `GBrainException` disables `S3925` (ISerializable) suppression rationale is specific to current architecture

- **File/Module:** `src/Services/LeanKernel.Services.Common/Memory/GBrainException.cs:6`
- **Observation:** The `#pragma warning disable S3925` comment says "Not used for binary serialization". This is correct for the current microservice deployment, but if the exception ever crosses process boundaries (e.g., gRPC, distributed tracing serialization), the missing `ISerializable` implementation will cause silent data loss.
- **Recommendation:** Either implement `ISerializable` properly or add a more specific justification comment explaining when this exception is guaranteed to stay in-process.

---

## Final Verdict

**No-Go** until C1, C2, M1, and M2 are addressed.

C1 (cancellation token ignored) is a production reliability risk under load. C2 (namespace mismatch) will cause maintenance friction and potential assembly-load issues. M1 (cross-tenant job leak) is a multi-tenant security boundary violation. M2 (false-positive name gap detection) degrades the onboarding experience. M3 (naive sentence splitting) produces garbage facts for any text containing abbreviations, URLs, or numeric separators. M4 (no circuit breaker) causes log flooding and CPU waste during DB outages.

The remaining S1-S8 items are hardening suggestions that can be addressed as follow-up work without blocking the phase gate.
