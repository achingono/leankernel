# Phase 2 Configuration

This reference lists the implemented Phase 2 settings added on top of the Phase 1 runtime configuration.

## LeanKernel:Identity

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Identity:AgentProfilePageKey` | string | `identity-agent-main` | GBrain page key used for the durable agent profile. |
| `LeanKernel:Identity:UserPreferencePageKey` | string | `identity-user-default` | GBrain page key used for the default user preference page. |
| `LeanKernel:Identity:OnboardingConfidenceThreshold` | number | `0.6` | Confidence floor below which onboarding guidance is emitted. |
| `LeanKernel:Identity:MaxOnboardingQuestionsPerTurn` | integer | `2` | Maximum number of onboarding questions added to one turn. |
| `LeanKernel:Identity:EnableIdentityExtraction` | boolean | `true` | Enables best-effort post-turn identity writeback. |
| `LeanKernel:Identity:AllowedIdentityFields` | string array | `preferred_name`, `timezone`, `locale`, `communication_style`, `work_style`, `recurring_goals`, `tool_preferences`, `autonomy_level` | Allowlist used for gap detection and writeback. |

## LeanKernel:Retrieval

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Retrieval:ScopingEnabled` | boolean | `true` | Enables scoped retrieval through `IScopedKnowledgeService`. |
| `LeanKernel:Retrieval:DefaultScope` | string | `global` | Scope used when request metadata does not specify one. |
| `LeanKernel:Retrieval:MaxEntityExpansionResults` | integer | `5` | Maximum related candidates discovered during entity expansion. |
| `LeanKernel:Retrieval:EntityBoostMultiplier` | number | `1.5` | Multiplier applied to entity-matching candidates. |
| `LeanKernel:Retrieval:MinScopeRelevanceScore` | number | `0.3` | Global score floor applied after scoped adjustments. |
| `LeanKernel:Retrieval:EmitRetrievalDiagnostics` | boolean | `true` | Includes per-candidate retrieval decisions and expanded entities in diagnostics. |
| `LeanKernel:Retrieval:ScopePolicies` | array | empty | Named include/exclude namespace and metadata rules. |

### LeanKernel:Retrieval:ScopePolicies

`ScopePolicies` is an array of deterministic retrieval rules.

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Name` | string | none | Stable scope identifier such as `global` or `personal`. |
| `IncludeNamespaces` | string array | empty | Allowlist of namespaces admitted by the policy. |
| `ExcludeNamespaces` | string array | empty | Denylist of namespaces rejected by the policy. |
| `RequiredMetadataKeys` | string array | empty | Candidate metadata keys that must be present. |
| `MinScore` | number | `0.0` | Policy-specific score floor merged with the global scoped floor. |

Request metadata can override the effective scope with `retrieval_scope`, `task_scope`, or `agent_scope`, in that precedence order.

Entity expansion depth still comes from `LeanKernel:Context:EntityExpansionDepth` and defaults to `2`.

## LeanKernel:History

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:History:RecentTurnsVerbatim` | integer | `6` | Newest turns that always remain verbatim. |
| `LeanKernel:History:CompactedTurnsMax` | integer | `10` | Next-oldest turns eligible for compaction. |
| `LeanKernel:History:SummarizedTurnsMax` | integer | `20` | Older turns eligible for summarization. |
| `LeanKernel:History:EnableCompaction` | boolean | `true` | Enables the compacted tier. |
| `LeanKernel:History:EnableSummarization` | boolean | `true` | Enables the summarized tier. |
| `LeanKernel:History:CompactionModel` | string | `gpt-4o-mini` | LiteLLM route used for compaction and summarization. |
| `LeanKernel:History:CompactionTemperature` | number | `0.1` | Low temperature used to keep shaping output stable. |
| `LeanKernel:History:MaxSummaryTokens` | integer | `200` | Maximum tokens returned for one compacted or summarized segment. |
| `LeanKernel:History:PersistCompactionMarkers` | boolean | `true` | Persists compaction markers when EF persistence is available. |

## LeanKernel:Channels

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Channels:Enabled` | boolean | `true` | Enables channel hosted-service startup and inbound routing. |
| `LeanKernel:Channels:Signal` | object | see below | Signal adapter settings. |
| `LeanKernel:Channels:ChannelAuth` | array | empty | Per-channel sender authorization rules. |

### LeanKernel:Channels:Signal

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Channels:Signal:Enabled` | boolean | `false` | Enables Signal adapter registration. |
| `LeanKernel:Channels:Signal:DaemonUrl` | string | `http://signal:8080` | Base URL for the Signal HTTP daemon bridge. |
| `LeanKernel:Channels:Signal:PhoneNumber` | string | empty | Signal account number used for polling and sends. |
| `LeanKernel:Channels:Signal:PollIntervalSeconds` | integer | `2` | Long-poll timeout passed to `/v1/receive/{account}`. |
| `LeanKernel:Channels:Signal:ReconnectDelaySeconds` | integer | `5` | Base reconnect delay after polling failures. |
| `LeanKernel:Channels:Signal:MaxReconnectAttempts` | integer | `10` | Maximum consecutive polling failures before polling stops. |

### LeanKernel:Channels:ChannelAuth

`ChannelAuth` is an array of per-channel sender rules.

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `ChannelId` | string | none | Channel identifier, such as `signal`. |
| `AllowedSenders` | string array | empty | Allowed sender ids when auth is required. |
| `RequireAuth` | boolean | `true` | When `true`, only configured senders may use the channel. |

## LeanKernel:Diagnostics

Phase 2 extends diagnostics with persisted context snapshots and dedicated diagnostics endpoints.

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `LeanKernel:Diagnostics:Enabled` | boolean | `true` | Enables diagnostics behavior globally. |
| `LeanKernel:Diagnostics:PersistToDatabase` | boolean | `true` | Persists diagnostic entries through the configured sink. |
| `LeanKernel:Diagnostics:ContextDiagnosticsEnabled` | boolean | `true` | Enables context snapshot storage and the dedicated context/budget/history diagnostics endpoints. |
| `LeanKernel:Diagnostics:MaxDiagnosticsPerSession` | integer | `100` | Caps how many recent context snapshots are considered per session. |
| `LeanKernel:Diagnostics:ServiceName` | string | `leankernel` | Service name used for diagnostics and log enrichment metadata. |

## Related documentation

- [Identity and Onboarding](../features/identity-onboarding.md)
- [Scoped Retrieval](../features/scoped-retrieval.md)
- [History Shaping](../features/history-shaping.md)
- [Channels](../features/channels.md)
- [Context Diagnostics API](../features/context-diagnostics-api.md)
- [Phase 1 Configuration](phase-1-config.md)
- [README](../../README.md)
