# Runtime skill system — review and remediation plan

## Problem

Review the runtime skill system, identify architectural gaps, and define how skill-required executables should be provisioned now that skills live in `SKILL.md` files instead of compiled code. The redesign must:

- balance **human-accessibility** (skills authored as readable markdown) and **determinism** (the runtime invokes exactly one well-defined operation),
- balance **security** (no arbitrary process execution, no SSRF, no secret leakage) and **convenience** (adding a skill should not require rebuilding the image whenever possible).

## Current state

- Skills are discovered from the filesystem by `RuntimeSkillRegistry` and turned into tools by `DynamicSkillToolFactory` / `DynamicPluginHost`.
- Skill metadata includes a `requires.bins` hint in several skills (`ms-todo`, `simplefin`, `screenshot-ocr`), but the runtime system does not consume it anywhere.
- The container build installs `signal-cli-native` only. There is no install path for `ms-todo-cli`, `simplefin-cli`, `paddleocr`, or any future skill binary.
- Skill directories currently contain only `SKILL.md`; binaries are **not** copied into or resolved from the same folder.
- The codebase carries a legacy nested-metadata key (a vestige of an earlier project name) that must be removed in favor of a flat `metadata` map plus a structured `runtime` block.

## Gaps

### A. Skill contract / parsing

1. **Metadata layout drift.** `SkillParser` reads `baseUrl`, `cliCommand`, and `authType` from the `metadata` root, but reads `emoji`/`homepage` from a nested vendor key. Skill files use the nested form for everything. Result: `baseUrl`/`cliCommand`/`authType` are silently `null` for every existing skill.
2. **`requires.bins` is documentation-only.** No code reads it. The runtime cannot tell whether a CLI dependency is satisfied.
3. **Operation extraction is prose-scraping.** Operation names come from `###` headings; endpoints are pulled out of `curl` examples with regex; HTTP method is sniffed from the presence of `-X POST`. This is brittle, untestable, and conflates docs with contract.
4. **Per-operation parameter schema is missing.** Agents only ever see one schema (`{"operation": string}`). The model has no way to know that `task list` accepts `--list-id`, that `list-id` is required, or what type it is. This is the single biggest contributor to failed tool calls.
5. **No validation at load time.** A malformed SKILL.md is silently downgraded to a partially-populated definition; the only signal is that operations later fail with "Unknown operation".
6. **Authoring redundancy.** The same fact (e.g., the `simplefin-cli account list` command) is currently expressed in prose, in a code block, and inferred from heading text. Authors have no single source of truth.

### B. Runtime registration / lifecycle

7. **Background fire-and-forget initialization.** `DynamicPluginHost.InitializeAsync()` is launched with `_ = Task.Run(...)` from a DI factory. The first agent request can hit an empty registry.
8. **Cache invalidation does not refresh tools.** `OnSkillFileChanged` clears the definition cache but never rebuilds `DynamicPluginHost._tools`, so live edits do not take effect until process restart.
9. **No file-watch debounce.** A single editor save can fire `Changed`+`Created` storms, each invalidating cache.
10. **Built-in tools are not merged.** `WikiQueryTool`, `KnowledgeSearchTool`, etc. are registered as `ITool`, but `DynamicPluginHost` only exposes dynamic skills. Agents lose access to built-ins.
11. **Name-collision behavior is undefined.** Two skill directories with the same `name:` produce a non-deterministic winner (last-write-wins inside the dictionary, but iteration order across base paths is not guaranteed).
12. **Stale tool references.** Agents may capture an `ITool` instance from a previous load; there is no instance refresh contract.

### C. Agent / tool wiring

13. **`AllowedTools` references obsolete names.** `WorkerAgent` allowlists `simplefin_skill`, `mstodo_skill`, etc. Dynamic tools register as `simplefin`, `ms-todo`. Allowlists never match.
14. **Allowlist is never enforced.** `ToolFunctionAdapter.BuildTools()` exposes the entire registry to every agent regardless of `AllowedTools`.
15. **No category / capability indexing.** Routing by intent (e.g., "financial skills") requires a tag taxonomy that the metadata model does not yet have.

### D. Security

16. **Process invocation is shell-injection-prone.** `DynamicSkillTool.ExecuteCommand` builds `ProcessStartInfo.Arguments` by string-concatenating quoted JSON values. Any quote, backtick, or `$(...)` in a parameter value compromises the host. Should use `ArgumentList`.
17. **No executable allowlist.** The CLI command name comes straight from a markdown file on disk; whoever can write to the skills directory can execute any binary on the image.
18. **SSRF surface.** `BaseUrl` is whatever the SKILL.md says. A hostile or careless skill can target `http://169.254.169.254/`, `file://`, or internal services.
19. **Auth not wired despite being parsed.** `authType: bearer` / `api_key` is read but never applied to outgoing HTTP requests; secrets have no per-skill scoping mechanism.
20. **Output is unbounded.** `StandardOutput.ReadToEndAsync()` will load arbitrary CLI output into memory; combined with `WaitForExitAsync` before reading, large stderr can deadlock the pipe.
21. **Whole input JSON is forwarded.** `BuildRequestContent` serializes the entire root including the `operation` discriminator, leaking internal routing fields to upstream APIs.
22. **No sandboxing.** CLI subprocesses inherit the host process user, env, and filesystem.

### E. Provisioning of executables

23. **No install path.** No Dockerfile layer, no manifest, no runtime install hook supports `requires.bins`.
24. **No version pinning or integrity check.** Even if installed, there is no record of expected version or checksum.
25. **No graceful degradation.** A skill whose binary is missing should be marked unavailable at load time, not fail at first invocation with a confusing `Win32Exception`.

### F. Observability

26. **No invocation audit.** Skill calls are not logged with operation, parameter shape (redacted), duration, or outcome at a structured level.
27. **No metrics.** Cannot answer "which skills are failing" or "which skills are unused".

## Design principles

These principles drive every recommendation below.

1. **Contract in frontmatter, prose in markdown.** Anything the runtime depends on (operation id, command template, parameter schema, required bins) lives in structured frontmatter. Markdown body is for the LLM and for human readers — never parsed for behavior.
2. **One source of truth per fact.** Each operation appears once in frontmatter; the markdown may *reference* it by id but does not redefine it.
3. **Fail loud at load time, not at first call.** Validation, binary availability, schema compilation all happen during discovery. Invalid skills are quarantined with a logged reason.
4. **Least privilege by default.** No skill can execute outside an explicit allowlist of binaries and an explicit allowlist of HTTP hosts.
5. **Convenience without sacrificing security.** Default path is image-managed (deterministic, signed, auditable). An optional, explicitly opt-in runtime install path allows experimentation, but only with pinned versions and checksums against a trusted registry.
6. **Hot reload is a first-class lifecycle.** The registry, tool instances, and DI consumers all observe a single "skills changed" event.

## Skill format (target)

`SKILL.md` keeps a single YAML frontmatter block. The `metadata` map carries presentation/discovery hints; `runtime` carries the deterministic contract; the markdown body is documentation.

```yaml
---
name: simplefin
description: "Inspect SimpleFin Bridge accounts and transactions."
metadata:
  emoji: "💸"
  homepage: "https://github.com/achingono/simplefin-cli"
  category: financial
  tags: [finance, accounts, transactions, read-only]
runtime:
  type: cli                      # cli | http | composite
  command: simplefin-cli         # required for cli/composite
  baseUrl: null                  # required for http/composite
  auth:
    type: none                   # none | bearer | apiKey | header
    secretRef: null              # logical name resolved against secret store
  requires:
    bins:
      - name: simplefin-cli
        minVersion: "0.4.0"
  egress:
    allowHosts: []               # http skills must declare; empty == disallow
operations:
  - id: status
    summary: "Show CLI configuration and link status."
    invoke:
      argv: [status]
    parameters:
      type: object
      properties: {}
      additionalProperties: false
  - id: list_accounts
    summary: "List all linked accounts."
    invoke:
      argv: [account, list]
    parameters:
      type: object
      properties: {}
      additionalProperties: false
  - id: list_transactions
    summary: "List transactions, optionally filtered."
    invoke:
      argv: [transaction, list]
      flags:
        accountId: "--account-id"
        startDate: "--start-date"
        endDate:   "--end-date"
    parameters:
      type: object
      properties:
        accountId: { type: string }
        startDate: { type: string, format: date }
        endDate:   { type: string, format: date }
      additionalProperties: false
---

# SimpleFin Bridge

Human-readable playbook follows…
```

Notes:
- `runtime.type` and `operations[].invoke` give the runtime everything it needs without scraping markdown.
- `parameters` is JSON Schema per operation — surfaced verbatim to the LLM, validated before execution.
- `argv` is a list (no shell), eliminating injection.
- `flags` maps parameter name → CLI flag, so rendering is declarative.
- `egress.allowHosts` constrains HTTP skills.
- `requires.bins` is consumed at load time.

The markdown body remains the human/LLM playbook. Operation IDs in the body link to the contract above.

## Binary provisioning strategy

A two-tier model balances security and convenience.

### Tier 1 — Image-managed (default, required for production)

- Required binaries are installed by the `Dockerfile` into `/opt/LeanKernel/tools/<name>/<version>/` and symlinked into `PATH`.
- Each install step records:
  - upstream source (URL or package),
  - pinned version,
  - SHA256 checksum verified at build time.
- A generated `tools-manifest.json` lists installed name/version/path; the registry consults it during skill load.
- A skill whose `requires.bins` cannot be satisfied is quarantined and logged; it does not fail the host startup.

### Tier 2 — Runtime install (opt-in, dev/experimental)

- Disabled by default. Enabled per environment via `LeanKernel:Skills:AllowRuntimeInstall=true`.
- Skills may declare an `install:` block:
  ```yaml
  runtime:
    requires:
      bins:
        - name: my-cli
          minVersion: "1.2.0"
          install:
            kind: npm | pip | github-release | script
            ref: "@scope/my-cli@1.2.0"
            sha256: "…"
  ```
- A `SkillInstaller` resolves the install kind against a fixed allowlist of trusted publishers (npm registry, PyPI, github.com/<allowedOrgs>). Checksum is mandatory.
- Installs land in a writable per-user prefix (`~/.LeanKernel/tools/`) that is **not** on the system `PATH`; the registry resolves binary paths explicitly per skill.
- Runtime install is logged as an audit event.

### Sandboxing (cross-cutting)

- All CLI subprocesses run with:
  - `ArgumentList` (no shell),
  - bounded stdout/stderr (configurable, default 1 MiB) read concurrently to avoid pipe deadlock,
  - hard wall-clock timeout (default 30 s, overridable per skill),
  - filtered environment (only vars declared in `runtime.env` are passed through),
  - working directory set to a per-invocation temp dir cleaned up on exit.
- HTTP egress goes through a single `HttpMessageHandler` that enforces `egress.allowHosts`.
- Auth secrets are resolved via `ISecretProvider` keyed by `auth.secretRef`; secrets never appear in the SKILL.md.

## Implementation phases

### Phase 1 — Strip the legacy nested-metadata key

- Rename folder `src/LeanKernel.Plugins/BuiltIn/OpenclaSkills` → `Skills`.
- Rename namespace `LeanKernel.Plugins.BuiltIn.OpenclaSkills` → `LeanKernel.Plugins.BuiltIn.Skills`.
- Drop the nested vendor key from the parser; read flat `metadata` and a new `runtime` block.
- Update existing SKILL.md files to the flat layout.
- Update `docs/SKILL_FORMAT.md` and any other documentation.

### Phase 2 — Typed contract + structured operations

- Introduce `SkillManifest`, `SkillRuntime`, `SkillOperation`, `SkillParameters` records.
- Replace prose-scraping in `SkillParser` with deserialization of the `runtime:` and `operations:` blocks.
- Validate at load time: required fields, JSON Schema compile, host allowlist non-empty for HTTP, every flag mapped to a declared parameter.
- Quarantine invalid skills with a structured error surfaced via an admin endpoint.

### Phase 3 — Binary provisioning (Tier 1)

- Add Dockerfile layers for `ms-todo-cli`, `simplefin-cli`, `paddleocr`, each with pinned version + SHA256.
- Emit `/opt/LeanKernel/tools/tools-manifest.json` during build.
- Implement `IBinaryResolver` consumed by `DynamicSkillTool` to resolve `requires.bins[].name` → absolute path.
- Skills missing a required bin are marked `Unavailable` and excluded from agent tool lists.

### Phase 4 — Secure execution

- Switch `DynamicSkillTool` to `ProcessStartInfo.ArgumentList`.
- Implement bounded, concurrent stdout/stderr reads with size cap.
- Add `IEgressPolicy` enforced via a typed `HttpClient` per skill.
- Wire `auth.type` + `auth.secretRef` to outbound requests through `ISecretProvider`.
- Drop the `operation` discriminator from outbound HTTP bodies; serialize only declared parameters.

### Phase 5 — Deterministic loading + hot reload

- Replace background `InitializeAsync` with synchronous load during `IHostedService.StartAsync`.
- Merge built-in `ITool` registrations and dynamic skills into a single `IToolRegistry` with deterministic precedence rules (built-ins win on collision; warning logged).
- Add debounced (250 ms) file-watch handler that rebuilds tool instances atomically and raises `SkillsChanged` for downstream consumers.
- Define `ISkillLifecycleListener` so agents/orchestrators refresh allowlists on change.

### Phase 6 — Agent allowlist consistency

- Normalize tool naming (skill `name:` *is* the tool name, no `_skill` suffix).
- Enforce `WorkerAgent.AllowedTools` inside `ToolFunctionAdapter.BuildTools()`.
- Support category-based selection (`AllowedCategories: [financial]`) in addition to explicit names.
- Update `WorkerAgent` defaults to current skill names (`simplefin`, `ms-todo`, `doughray`, `emanate`, `screenshot-ocr`).

### Phase 7 — Tier 2 runtime install (opt-in)

- Implement `SkillInstaller` with kind allowlist, mandatory checksum, per-user prefix.
- Surface install state (`pending`, `installed`, `failed`) in the registry.
- Add admin endpoint to trigger/refresh installs without restart.

### Phase 8 — Observability

- Structured logging for every skill invocation: skill, operation, parameter keys (values redacted by default), duration, exit code/HTTP status, output size.
- Counters: invocations, failures, quarantined skills, missing bins.
- Admin/debug endpoint listing loaded skills, their resolved bin paths, validation status, last reload time.

### Phase 9 — Tests + docs

- Unit tests: frontmatter parsing, schema compilation, validation errors, allowlist enforcement, argv rendering, egress policy, secret redaction.
- Integration tests: end-to-end skill discovery → invocation with a fixture CLI; hot reload; missing-bin quarantine.
- Update `docs/SKILL_FORMAT.md` with the new contract and a migration note for the old layout.

## Key files to change

- `src/LeanKernel.Plugins/BuiltIn/Skills/SkillDefinition.cs` — typed manifest model, per-operation schema, runtime block.
- `src/LeanKernel.Plugins/BuiltIn/Skills/SkillParser.cs` — structured deserialization, no prose scraping.
- `src/LeanKernel.Plugins/BuiltIn/Skills/RuntimeSkillRegistry.cs` — synchronous load, debounced watcher, quarantine list, lifecycle event.
- `src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs` — `ArgumentList`, bounded IO, egress + auth wiring.
- `src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillToolFactory.cs` — bin resolution + availability checks at factory time.
- `src/LeanKernel.Plugins/BuiltIn/Skills/IBinaryResolver.cs` (new) — manifest-backed resolver.
- `src/LeanKernel.Plugins/BuiltIn/Skills/IEgressPolicy.cs` (new) — host allowlist enforcement.
- `src/LeanKernel.Plugins/BuiltIn/Skills/ISecretProvider.cs` (new) — pluggable secret backend.
- `src/LeanKernel.Host/Program.cs` — replace fire-and-forget init with hosted service; merge built-ins + dynamic into one registry.
- `src/LeanKernel.Thinker/Agents/WorkerAgent.cs` — updated allowlists, category support.
- `src/LeanKernel.Thinker/ToolFunctionAdapter.cs` — enforce per-agent allowlist.
- `Dockerfile` — install pinned, checksummed CLIs; emit `tools-manifest.json`.
- `docs/SKILL_FORMAT.md` — new contract, migration note.
- All `.github/skills-remote/*/SKILL.md` — migrated to flat metadata + structured `runtime`/`operations`.

## Migration order (recommended)

1. Phase 1 (legacy rename) — small, mechanical, unblocks everything.
2. Phase 2 (typed contract) — required before any other phase has a stable surface to build on.
3. Phase 3 (Tier 1 provisioning) and Phase 4 (secure execution) in parallel; both are independent of each other but both need Phase 2.
4. Phase 5 (lifecycle) — once tool instances are first-class.
5. Phase 6 (agent wiring) — depends on stable tool names.
6. Phase 7 (Tier 2 install) — opt-in, can ship later.
7. Phase 8 + 9 (observability + tests + docs) — continuous, but a focused pass at the end ensures coverage.

## Notes

- This plan removes the legacy nested vendor key from the runtime contract. SKILL.md authors migrate by moving fields up one level and adding the new `runtime`/`operations` blocks.
- The two-tier provisioning model is intentional: Tier 1 is the only path enabled in production; Tier 2 unlocks fast iteration in dev without becoming a backdoor.
- Sandboxing here is process-level (argument list, bounded IO, env filtering, egress allowlist). Stronger isolation (seccomp, per-skill containers) is a future option once the contract stabilizes.
