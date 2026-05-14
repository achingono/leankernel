# Skill Definition Format (`SKILL.md`)

LeanKernel dynamic skills are discovered from `SKILL.md` files and parsed by `SkillParser`.

## Minimum Required Frontmatter

```yaml
---
name: my_skill
description: "What the skill does"
runtime:
  type: cli            # cli | http | composite
  command: my-cli      # required for cli/composite
  auth:
    type: none         # none | bearer | apiKey | header
    secretRef: null
  requires:
    bins:
      - name: my-cli
        minVersion: "1.0.0"
  egress:
    allowHosts: []     # required (non-empty) for http skills
operations:
  - id: do_work
    summary: "Run the primary operation"
    invoke:
      argv: [do, work]
      flags:
        target: "--target"
    parameters:
      type: object
      properties:
        target:
          type: string
---
```

## Parsed Contract

`SkillParser` and `SkillDefinition` currently support:

- `name`, `description`
- `metadata` (arbitrary dictionary)
- `runtime`
  - `type`, `command`, `baseUrl`, `timeoutSeconds`
  - `auth.type`, `auth.secretRef`
  - `requires.bins[]` (`name`, `minVersion`, optional `checksumSha256`)
  - `egress.allowHosts[]`
- `operations[]`
  - `id`, `summary`
  - `invoke.argv[]`, `invoke.flags{}`, optional `invoke.httpMethod`, `invoke.httpPath`
  - `parameters` (JSON-schema-like object)

## Validation Rules

Current parser validation includes:

- `name` and `description` must exist
- `runtime` block is required
- HTTP skills require non-empty `runtime.egress.allowHosts`
- CLI/composite skills require `runtime.command`
- At least one operation is required
- Each operation requires `id`, `summary`, and `invoke`
- If `parameters` exists, it must contain `type: object`
- `invoke.flags` keys must map to declared `parameters.properties`

Invalid skills are quarantined by `RuntimeSkillRegistry` and excluded from active tool registration.
Quarantined skills are tracked in-memory and surfaced via logs; they are not loaded as tools. To inspect quarantined skills, check application logs for warnings from `RuntimeSkillRegistry`. The `GetQuarantinedSkills()` method is available in code but is not exposed as a public API endpoint by default.

## Runtime Loading and Hot Reload

1. `SkillHostedService` initializes registry and plugin host at startup.
2. `RuntimeSkillRegistry` discovers and caches skill definitions.
3. `DynamicPluginHost` merges built-in tools with available dynamic skills.
4. File watchers (debounced 250ms) detect `SKILL.md` changes and refresh registry/plugin host.

## Binary Availability

If a skill declares `runtime.requires.bins`, each binary is checked via `IBinaryResolver`.  
Skills with missing required binaries are marked unavailable and skipped during registration.

## Security Notes (Current)

- CLI invocation uses explicit argument handling in dynamic tool runtime.
- HTTP calls run through an egress policy handler using declared `allowHosts`.
- Skill-specific timeout is supported (`runtime.timeoutSeconds`, default 30s in factory-created client).
