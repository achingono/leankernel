# Data Model

This reference defines the **target persistence model** for the LeanKernel rearchitecture project, including Postgres tables and GBrain wiki page conventions.

## Postgres tables

### `sessions`

| Column | Type | Purpose |
|--------|------|---------|
| `id` | UUID | Stable session identifier |
| `channel_id` | text | External channel or transport identifier |
| `user_id` | text | Canonical user identity key |
| `created_at` | timestamptz | Session creation time |
| `updated_at` | timestamptz | Last write time for the session |
| `metadata` | jsonb | Channel-specific and workflow-specific metadata |

### `turns`

| Column | Type | Purpose |
|--------|------|---------|
| `id` | UUID | Stable turn identifier |
| `session_id` | UUID | Foreign key to `sessions.id` |
| `role` | text | Turn role such as `user`, `assistant`, or `system` |
| `content` | text | Canonical turn body |
| `timestamp` | timestamptz | Turn creation time |
| `is_compacted` | boolean | Indicates whether the turn content is summarized or compressed |
| `compaction_source_id` | UUID nullable | Links a compacted turn to the original source turn or summary group |

### `capability_gaps`

| Column | Type | Purpose |
|--------|------|---------|
| `id` | UUID | Stable capability-gap identifier |
| `category` | text | Gap class such as tool, knowledge, policy, or workflow |
| `description` | text | Human-readable gap statement |
| `detected_at` | timestamptz | Detection time |
| `session_id` | UUID nullable | Optional foreign key to the originating session |
| `resolved_at` | timestamptz nullable | Resolution time when the gap is closed |

### `diagnostic_entries`

| Column | Type | Purpose |
|--------|------|---------|
| `id` | UUID | Stable diagnostic event identifier |
| `session_id` | UUID nullable | Associated session when available |
| `turn_id` | UUID nullable | Associated turn when available |
| `category` | text | Diagnostic stream such as audit, trace, routing, or policy |
| `payload` | jsonb | Structured event payload |
| `timestamp` | timestamptz | Event time |

### `scheduled_jobs`

| Column | Type | Purpose |
|--------|------|---------|
| `id` | UUID | Stable job identifier |
| `name` | text | Operator-facing job name |
| `cron_expression` | text | Cron schedule |
| `last_run` | timestamptz nullable | Most recent execution time |
| `next_run` | timestamptz nullable | Next calculated execution time |
| `enabled` | boolean | Enables or disables execution |
| `config` | jsonb | Job-specific settings |

## Relational rules

- `sessions` is the root table for conversational history.
- `turns.session_id` references `sessions.id`.
- `diagnostic_entries` may point at both `sessions` and `turns` for traceability.
- `capability_gaps.session_id` is optional because some gaps can be detected outside a single live conversation.
- `scheduled_jobs` is intentionally independent so background work can survive session deletion.

## GBrain wiki page conventions

### User identity pages

- Path: `people/{user-id}.md`
- Purpose: stable user profile grounded in structured metadata
- Expected metadata: preferences, communication style, interests

Example:

```md
---
id: user-123
preferences:
  tone: concise
communication_style: direct
interests:
  - ai agents
  - dotnet
---
```

### Agent identity pages

- Path: `agents/{agent-name}.md`
- Purpose: operational identity for an agent persona or runtime role
- Expected metadata: system prompt, capabilities, constraints

### Knowledge pages

- Use standard GBrain page structure.
- Include a timeline section for material facts that change over time.
- Include explicit cross-references so related pages can be traversed without a semantic search round-trip.

## Page naming and cross-linking

- Use lowercase, stable identifiers in paths.
- Prefer hyphenated slugs for names that are not already stable IDs.
- Keep one canonical page per person, agent, or durable concept.
- Link pages using direct relative references whenever a relationship is intentional and durable.
- Cross-link identity pages to relevant knowledge pages, and knowledge pages back to identity pages when the relationship is user-specific or agent-specific.
- Avoid duplicate pages that differ only by spelling, casing, or channel-specific aliases; record aliases in metadata instead.
