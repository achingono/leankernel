# Phase 3 Configuration

This reference covers the full Phase 3 configuration surface for routing, orchestration, enhancement, learning, scheduling, and production hardening.

Defaults below reflect the shipped Gateway configuration in `src/LeanKernel.Gateway/appsettings.json` unless noted otherwise. Some collection properties are empty in the C# config types but populated with disabled sample entries in appsettings so operators can see the expected shape.

## LeanKernel:Routing

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Routing:Enabled` | boolean | `false` | Enables `RoutedAgentStrategy` instead of the default static single-model path. |
| `LeanKernel:Routing:QualityMinOutputLength` | integer | `50` | Minimum response length required by the routed quality gate. |
| `LeanKernel:Routing:QualityMinConstraintCoverage` | number | `0.6` | Minimum normalized constraint coverage required by the routed quality gate. |
| `LeanKernel:Routing:MaxEscalationAttempts` | integer | `2` | Maximum number of forward tier escalations after a failed quality check. |
| `LeanKernel:Routing:RefusalPatterns` | string array | default list below | Case-insensitive phrases used by refusal detection in both quality gates and shadow comparison. |
| `LeanKernel:Routing:ShadowRoutingEnabled` | boolean | `false` | Wraps the authoritative strategy in `ShadowRoutingStrategy`. |
| `LeanKernel:Routing:ShadowModel` | string | empty | LiteLLM model route used for the non-authoritative shadow invocation. |

Default `RefusalPatterns` values:

- `I cannot`
- `I'm sorry, I can't`
- `As an AI language model`
- `I'm not able to`
- `I don't have the ability`

### LeanKernel:Routing tier settings

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Routing:Economy:Model` | string | `gpt-4o-mini` | Model route used for low-complexity turns. |
| `LeanKernel:Routing:Economy:MaxTokens` | integer | `4096` | Completion budget metadata for the economy tier. |
| `LeanKernel:Routing:Economy:CostWeight` | number | `0.3` | Relative cost weight for the economy tier. |
| `LeanKernel:Routing:Standard:Model` | string | `gpt-4o` | Model route used for medium-complexity turns. |
| `LeanKernel:Routing:Standard:MaxTokens` | integer | `8192` | Completion budget metadata for the standard tier. |
| `LeanKernel:Routing:Standard:CostWeight` | number | `1.0` | Relative cost weight for the standard tier. |
| `LeanKernel:Routing:Premium:Model` | string | `claude-sonnet-4-20250514` | Model route used for high-complexity or escalated turns. |
| `LeanKernel:Routing:Premium:MaxTokens` | integer | `16384` | Completion budget metadata for the premium tier. |
| `LeanKernel:Routing:Premium:CostWeight` | number | `3.0` | Relative cost weight for the premium tier. |

### LeanKernel:Routing:Scoring

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Routing:Scoring:HighComplexityTokenThreshold` | integer | `2000` | User-message token estimate at which the base complexity score becomes high. |
| `LeanKernel:Routing:Scoring:MediumComplexityTokenThreshold` | integer | `500` | User-message token estimate at which the base complexity score becomes medium. |
| `LeanKernel:Routing:Scoring:ToolUsageComplexityBoost` | number | `0.3` | Score boost scaled by tool count. |
| `LeanKernel:Routing:Scoring:MultiTurnComplexityBoost` | number | `0.2` | Score boost scaled by history length. |
| `LeanKernel:Routing:Scoring:LongContextComplexityBoost` | number | `0.2` | Score boost split across long history and long system prompt signals. |

## LeanKernel:Orchestration

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Orchestration:Enabled` | boolean | `false` | Enables coordinator-worker orchestration. |
| `LeanKernel:Orchestration:MaxWorkerConcurrency` | integer | `3` | Maximum number of worker tools the coordinator may invoke at once. |
| `LeanKernel:Orchestration:MaxOrchestrationDepth` | integer | `2` | Maximum nested orchestration depth allowed for a run. |
| `LeanKernel:Orchestration:WorkerTimeout` | duration | `00:01:00` | Timeout budget applied to each worker invocation. |
| `LeanKernel:Orchestration:Workers` | array | disabled examples in appsettings | Worker definitions available to the coordinator when orchestration is enabled. |

### LeanKernel:Orchestration:Workers[]

| Key | Type | Code default | Description |
| --- | --- | --- | --- |
| `Name` | string | required | Stable worker-tool name used by the coordinator. |
| `Description` | string | required | Human-readable worker purpose surfaced in coordinator instructions. |
| `Model` | string | `gpt-4o-mini` | LiteLLM model route used when the worker executes. |
| `SystemPrompt` | string | empty | Worker-specific system prompt. |
| `AllowedTools` | string array | empty | Explicit tool-name allowlist for the worker. |
| `AllowedCategories` | string array | empty | Tool-category allowlist applied when no explicit tool names are supplied. |
| `Scope` | string or null | `null` | Optional scope forwarded into tool visibility and worker instructions. |

The shipped appsettings examples define two disabled-by-default worker shapes:

- `researcher` — knowledge-focused worker with `wiki_search` and `wiki_read`
- `writer` — drafting worker with no tool access

## LeanKernel:Enhancement

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Enhancement:Enabled` | boolean | `true` | Enables the synchronous response enhancement pipeline. |
| `LeanKernel:Enhancement:KnowledgeSynthesisEnabled` | boolean | `true` | Registers `KnowledgeSynthesisStep`. |
| `LeanKernel:Enhancement:RefusalInterceptionEnabled` | boolean | `true` | Registers `RefusalInterceptionStep`. |
| `LeanKernel:Enhancement:CitationInjectionEnabled` | boolean | `false` | Registers `CitationInjectionStep`. |
| `LeanKernel:Enhancement:MaxEnhancementTimeMs` | integer | `5000` | Pipeline-wide timeout budget in milliseconds. |

## LeanKernel:Learning

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Learning:Enabled` | boolean | `true` | Enables the background learning subsystem. |
| `LeanKernel:Learning:FactExtractionEnabled` | boolean | `true` | Registers the LiteLLM-based fact extraction step. |
| `LeanKernel:Learning:CapabilityGapDetectionEnabled` | boolean | `true` | Registers the deterministic capability-gap step. |
| `LeanKernel:Learning:EngagementTrackingEnabled` | boolean | `true` | Registers the engagement metrics step. |
| `LeanKernel:Learning:MaxConcurrentLearningTasks` | integer | `2` | Maximum concurrent background learning tasks. |
| `LeanKernel:Learning:QueueCapacity` | integer | `100` | Bounded `TurnEventQueue` capacity before drop-oldest behavior begins. |
| `LeanKernel:Learning:ExtractionModel` | string | `gpt-4o-mini` | LiteLLM route used by `FactExtractionStep`. |
| `LeanKernel:Learning:ExtractionTemperature` | number | `0.1` | Low temperature used for fact extraction. |
| `LeanKernel:Learning:MinTurnLengthForExtraction` | integer | `50` | Combined user+assistant text threshold before fact extraction runs. |

## LeanKernel:Scheduler

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Scheduler:Enabled` | boolean | `false` | Enables `SchedulerHostedService`. |
| `LeanKernel:Scheduler:TickIntervalSeconds` | integer | `60` | Delay between scheduler ticks. |
| `LeanKernel:Scheduler:MaxConcurrentJobs` | integer | `2` | Maximum number of in-flight scheduled jobs. |
| `LeanKernel:Scheduler:DefaultTimezone` | string | `UTC` | Fallback timezone for cron and boundary evaluation. |
| `LeanKernel:Scheduler:Jobs` | array | disabled examples in appsettings | Scheduled job definitions evaluated on each tick. |

### LeanKernel:Scheduler:Jobs[]

| Key | Type | Code default | Description |
| --- | --- | --- | --- |
| `Name` | string | required | Unique job name. |
| `CronExpression` | string | required | Standard cron expression parsed by Cronos. |
| `JobType` | string | required | Supported values: `agent-prompt`, `knowledge-refresh`, `maintenance`. |
| `Prompt` | string or null | `null` | Prompt text for prompt-driven jobs or optional query text for refresh jobs. |
| `ChannelId` | string or null | `null` | Channel identifier used by `agent-prompt` jobs. |
| `UserId` | string or null | `null` | Sender identifier used by `agent-prompt` jobs. |
| `Enabled` | boolean | `true` | Per-job enable flag. The shipped appsettings examples set jobs to `false`. |
| `Parameters` | string dictionary | empty | Free-form job parameters interpreted by `JobExecutor`. |

### Common scheduler parameters

| Parameter | Used by | Meaning |
| --- | --- | --- |
| `timezone` | all job types | Overrides `DefaultTimezone` for cron and boundary evaluation. |
| `required_boundary` / `time_boundary` | all job types | Restricts execution to `Morning`, `Afternoon`, `Evening`, or `Night`. |
| `key` | `knowledge-refresh` | Refresh one knowledge page by exact key. |
| `query` | `knowledge-refresh` | Search query used when no exact key is supplied. |
| `max_results` | `knowledge-refresh` | Maximum search matches to refresh. |
| `task` | `maintenance` | `cleanup-old-diagnostics`, `cleanup-compaction-markers`, `cleanup-all`, or `knowledge-fact-defrag`. |
| `retention_days` | `maintenance` | Age threshold for cleanup tasks. |
| `scope_query` | `maintenance` (`knowledge-fact-defrag`) | Search scope used to discover candidate fact pages (default `learning/facts/`). |
| `max_candidates` | `maintenance` (`knowledge-fact-defrag`) | Upper bound for fact pages inspected per run (clamped to 1000). |
| `min_age_days` | `maintenance` (`knowledge-fact-defrag`) | Do not retire pages newer than this age threshold. |
| `normalization_mode` | `maintenance` (`knowledge-fact-defrag`) | `hybrid` (default) or `deterministic`; hybrid uses opt-in LLM repairs for partial 5W1H normalization. |
| `max_llm_repairs_per_run` | `maintenance` (`knowledge-fact-defrag`) | Maximum number of partial pages that can trigger LLM-assisted repair per run. |

`knowledge-fact-defrag` also rewrites scanned fact pages into a consistent 5W1H structure (`Who`, `What`, `When`, `Where`, `Why`, `How`) so new chat-generated pages converge toward uniform format over repeated runs. Missing fields are not filled with synthetic defaults; partial normalization is marked in-page and logged.

## LeanKernel:Hardening

### LeanKernel:Hardening:SpendGuard

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Hardening:SpendGuard:Enabled` | boolean | `false` | Enables spend-guard evaluation before model execution. |
| `LeanKernel:Hardening:SpendGuard:MaxDailySpendUsd` | number | `10.0` | Daily spend cap in USD. |
| `LeanKernel:Hardening:SpendGuard:MaxSessionSpendUsd` | number | `2.0` | Per-session spend cap in USD. |
| `LeanKernel:Hardening:SpendGuard:MaxMonthlySpendUsd` | number | `100.0` | Monthly spend cap in USD. |
| `LeanKernel:Hardening:SpendGuard:WarnAtPercent` | string | `80` | Warning threshold percentage before a hard block. |

### LeanKernel:Hardening:RateLimit

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Hardening:RateLimit:Enabled` | boolean | `true` | Enables Gateway request rate limiting. |
| `LeanKernel:Hardening:RateLimit:RequestsPerMinute` | integer | `30` | Per-caller sliding-window limit per minute. |
| `LeanKernel:Hardening:RateLimit:RequestsPerHour` | integer | `300` | Per-caller sliding-window limit per hour. |
| `LeanKernel:Hardening:RateLimit:ConcurrentRequests` | integer | `5` | Maximum concurrent requests per caller. |

### LeanKernel:Hardening:HealthTracking

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Hardening:HealthTracking:CheckIntervalSeconds` | integer | `30` | Background provider-health probe interval. |
| `LeanKernel:Hardening:HealthTracking:UnhealthyThreshold` | integer | `3` | Consecutive failures required before a provider becomes unhealthy. |
| `LeanKernel:Hardening:HealthTracking:HealthyThreshold` | integer | `2` | Consecutive successes required before a provider becomes healthy again. |

### LeanKernel:Hardening:Resilience

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Hardening:Resilience:RetryCount` | integer | `2` | Shared retry budget for resilient clients and probes. |
| `LeanKernel:Hardening:Resilience:RetryDelayMs` | integer | `1000` | Retry delay in milliseconds. |
| `LeanKernel:Hardening:Resilience:CircuitBreakerThreshold` | integer | `5` | Reserved circuit-breaker threshold for resilience policies. |
| `LeanKernel:Hardening:Resilience:CircuitBreakerDurationSeconds` | integer | `30` | Reserved circuit-breaker open duration. |
| `LeanKernel:Hardening:Resilience:TimeoutSeconds` | integer | `30` | Timeout budget used by health probes and resilient HTTP clients. |

## LeanKernel:Webwright

Browser automation is disabled by default and requires the `webwright` sidecar plus a shared bearer token.

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Webwright:Enabled` | boolean | `false` | Registers the `browser` tool category and enables authenticated sidecar readiness probing. |
| `LeanKernel:Webwright:BaseUrl` | string | `http://webwright:8000` | Root URL for the webwright sidecar. |
| `LeanKernel:Webwright:ApiToken` | string | empty | Bearer token used for `/ready` and browser run endpoints. |
| `LeanKernel:Webwright:RequestTimeoutSeconds` | integer | `15` | HTTP timeout for sidecar requests. Browser tasks are asynchronous, so this is not the Webwright wall-clock limit. |
| `LeanKernel:Webwright:MaxArtifactBytes` | integer | `2000000` | Maximum artifact bytes returned through `browser_get_artifact`. |
| `LeanKernel:Webwright:MaxOutputChars` | integer | `12000` | Maximum intended browser tool output characters. |
| `LeanKernel:Webwright:DefaultModel` | string | `gpt-4o` | Default LiteLLM model alias forwarded to browser task submissions. |
| `LeanKernel:Webwright:HealthProbe:Enabled` | boolean | `true` | Enables authenticated `/ready` checks when the browser service is enabled. |

Related Compose variables include `WEBWRIGHT_ENABLED`, `WEBWRIGHT_API_TOKEN`, `WEBWRIGHT_LITELLM_KEY`, `WEBWRIGHT_MODEL`, `WEBWRIGHT_DOMAIN_ALLOWLIST`, and `WEBWRIGHT_DOMAIN_DENYLIST`.

## Related Phase 3 operational keys outside LeanKernel:Hardening

OpenTelemetry export is configured at the top level, not under `LeanKernel:Hardening`.

| Key | Type | Gateway default | Description |
| --- | --- | --- | --- |
| `OpenTelemetry:ConsoleExporterEnabled` | boolean | `false` | Enables console exporters for traces, metrics, and logs. |
| `OpenTelemetry:Otlp:Endpoint` | string | empty | OTLP exporter endpoint for traces, metrics, and logs. |

## Related documentation

- [Phase 1 Configuration](phase-1-config.md)
- [Phase 2 Configuration](phase-2-config.md)
- [Model Routing](../features/model-routing.md)
- [Response Enhancement](../features/response-enhancement.md)
- [Scheduler](../features/scheduler.md)
- [Production Operations](../features/production-ops.md)
- [Browser Automation Tool](../features/browser-tool.md)
