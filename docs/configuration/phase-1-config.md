# Phase 1 Configuration

This reference lists the configuration currently used by the implemented Phase 1 runtime slices, including the `LeanKernel.Gateway` host.

## Serilog

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Serilog:MinimumLevel:Default` | string | `Information` (`Debug` in Development) | Baseline application log level. |
| `Serilog:MinimumLevel:Override:Microsoft.AspNetCore` | string | `Warning` | Reduces noisy request pipeline logs. |
| `Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore` | string | `Warning` | Reduces noisy EF Core logs. |
| `Serilog:WriteTo` | array | `Console` | Writes structured logs to the console sink. |

## LeanKernel:LiteLlm

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:LiteLlm:BaseUrl` | string | `http://litellm:4000` | OpenAI-compatible LiteLLM endpoint used by `AgentFactory`. |
| `LeanKernel:LiteLlm:ApiKey` | string | `sk-leankernel-local` (`sk-dev` in Development) | API key sent to the LiteLLM endpoint. |
| `LeanKernel:LiteLlm:DefaultModel` | string | `gpt-4o-mini` | Default model alias for the static agent strategy. |
| `LeanKernel:LiteLlm:ContextWindowTokens` | integer | `128000` | Total prompt window used for context budget calculation. |

## LeanKernel:Context

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Context:SystemPromptBudgetRatio` | number | `0.15` | Fraction of prompt budget reserved for the system prompt. |
| `LeanKernel:Context:WikiFactsBudgetRatio` | number | `0.20` | Fraction reserved for wiki facts. |
| `LeanKernel:Context:ConversationBudgetRatio` | number | `0.40` | Fraction reserved for recent conversation turns. |
| `LeanKernel:Context:RetrievalBudgetRatio` | number | `0.20` | Fraction reserved for retrieved context. |
| `LeanKernel:Context:ToolsBudgetRatio` | number | `0.05` | Fraction reserved for tool visibility/context. |
| `LeanKernel:Context:ResponseHeadroomRatio` | number | `0.25` | Fraction reserved for model output headroom. |
| `LeanKernel:Context:RecentTurnsVerbatim` | integer | `6` | Number of recent turns kept verbatim before compaction logic applies. |
| `LeanKernel:Context:CompactedTurnsMax` | integer | `10` | Upper bound for compacted older turns. |

## LeanKernel:Routing

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Routing:Enabled` | boolean | `false` | Switches `IAgentStrategy` from `StaticAgentStrategy` to `RoutedAgentStrategy`. |
| `LeanKernel:Routing:QualityMinOutputLength` | integer | `50` | Lower bound for acceptable output length before a routed attempt can be accepted. |
| `LeanKernel:Routing:QualityMinConstraintCoverage` | number | `0.6` | Minimum prompt-to-response coverage heuristic before a routed attempt is accepted. |
| `LeanKernel:Routing:MaxEscalationAttempts` | integer | `2` | Maximum number of tier escalations for one routed turn. |
| `LeanKernel:Routing:ShadowRoutingEnabled` | boolean | `false` | Wraps the authoritative strategy in `ShadowRoutingStrategy` so a secondary model is invoked in parallel for diagnostics only. |
| `LeanKernel:Routing:ShadowModel` | string | empty | Model alias used by the diagnostic-only shadow invocation when shadow routing is enabled. |
| `LeanKernel:Routing:Economy:Model` | string | `gpt-4o-mini` | Economy-tier model used for low-complexity turns. |
| `LeanKernel:Routing:Economy:MaxTokens` | integer | `4096` | Economy-tier token budget hint. |
| `LeanKernel:Routing:Economy:CostWeight` | number | `0.3` | Relative cost weight for the economy tier. |
| `LeanKernel:Routing:Standard:Model` | string | `gpt-4o` | Standard-tier model used for medium-complexity turns. |
| `LeanKernel:Routing:Standard:MaxTokens` | integer | `8192` | Standard-tier token budget hint. |
| `LeanKernel:Routing:Standard:CostWeight` | number | `1.0` | Relative cost weight for the standard tier. |
| `LeanKernel:Routing:Premium:Model` | string | `claude-sonnet-4-20250514` | Premium-tier model used for high-complexity turns and top-tier escalation. |
| `LeanKernel:Routing:Premium:MaxTokens` | integer | `16384` | Premium-tier token budget hint. |
| `LeanKernel:Routing:Premium:CostWeight` | number | `3.0` | Relative cost weight for the premium tier. |
| `LeanKernel:Routing:Scoring:HighComplexityTokenThreshold` | integer | `2000` | User-message token threshold that routes directly to the premium tier. |
| `LeanKernel:Routing:Scoring:MediumComplexityTokenThreshold` | integer | `500` | User-message token threshold that routes to the standard tier. |
| `LeanKernel:Routing:Scoring:ToolUsageComplexityBoost` | number | `0.3` | Score boost applied when tool usage is available for the turn. |
| `LeanKernel:Routing:Scoring:MultiTurnComplexityBoost` | number | `0.2` | Score boost applied for multi-turn conversation history. |
| `LeanKernel:Routing:Scoring:LongContextComplexityBoost` | number | `0.2` | Score boost applied for long history or complex system prompts. |

## LeanKernel:GBrain

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:GBrain:BaseUrl` | string | `http://gbrain:8789` (`http://localhost:8789` in Development) | Root MCP HTTP endpoint used by the knowledge service. |
| `LeanKernel:GBrain:AuthToken` | string | empty | Optional bearer token for the GBrain service. |
| `LeanKernel:GBrain:TimeoutSeconds` | integer | `30` | HTTP timeout for GBrain requests. |

## LeanKernel:Database

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Database:ConnectionString` | string | `Host=database;Database=leankernel;Username=leankernel;Password=leankernel` | PostgreSQL connection string for sessions and diagnostics. |

## LeanKernel:Diagnostics

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Diagnostics:Enabled` | boolean | `true` | Enables diagnostics collection for the runtime. |
| `LeanKernel:Diagnostics:PersistToDatabase` | boolean | `true` | Persists diagnostic entries through the configured sink. |
| `LeanKernel:Diagnostics:ContextDiagnosticsEnabled` | boolean | `true` | Enables persisted per-turn context snapshot reads and writes for the context diagnostics APIs. |
| `LeanKernel:Diagnostics:MaxDiagnosticsPerSession` | integer | `100` | Caps how many stored context snapshots are considered per session when resolving diagnostics. |
| `LeanKernel:Diagnostics:ServiceName` | string | `leankernel` | Service name used for diagnostics/log enrichment metadata. |

## LeanKernel:Gateway

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Gateway:ApiKey` | string | empty | Single accepted API key for chat and diagnostics endpoints. Empty means development-mode open access. |
| `LeanKernel:Gateway:ApiKeys` | string array | empty | Optional array form for multi-value environment overrides. When present, any listed key is accepted. |

## Related documentation

- [Gateway API](../features/gateway-api.md)
- [Phase 2 Configuration](phase-2-config.md)
- [Infrastructure](../architecture/infrastructure.md)
- [README](../../README.md)
