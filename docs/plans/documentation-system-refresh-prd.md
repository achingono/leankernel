# PRD: Documentation System Refresh and Information Architecture

## 1) Goal

Bring `README.md` and all documentation under `docs/` back into strict alignment with the current implementation, and reorganize docs into a highly accurate, structured, hyperlinked, and discoverable content system.

This plan defines the target documentation architecture, migration approach, quality gates, and rollout phases.

## 2) Current-State Findings (Drift Summary)

Based on repo inspection (`README.md`, `docs/`, `src/`, test projects, and workflow docs), the major drift patterns are:

1. **Topology drift**
   - Some docs still describe older sidecars/topologies and phase-era architecture language not matching the active runtime composition.
2. **Naming drift**
   - Project/module naming in narrative docs does not consistently match current `src/` project ownership boundaries.
3. **Duplication and overlap**
   - Multiple feature pages cover similar topics with different framing (for example routing/channel/UI variants), making discovery harder and accuracy inconsistent.
4. **Navigation quality issues**
   - Top-level indexes exist, but the overall path to answer common questions is not opinionated enough (install -> run -> configure -> debug -> extend).
5. **Planning artifacts mixed with product docs**
   - `docs/plans/` is large and flat; planning docs are valuable but compete with implementation docs for discoverability.

## 3) Design Principles

1. **Implementation-first truth**
   - If code and docs disagree, docs must change.
2. **Single canonical page per topic**
   - One owner page per capability; related pages link to it rather than restating.
3. **Progressive disclosure**
   - New users get concise runbooks; advanced users can drill down into architecture, APIs, and internals.
4. **Strict structure conventions**
   - Kebab-case file names, hierarchical folders, and `index.md` in every folder.
5. **Hyperlinked discoverability**
   - Every page includes: where to start, related pages, and source-of-truth links to code locations.

## 4) Target Documentation Architecture

All folders include `index.md`.

```text
docs/
  index.md
  getting-started/
    index.md
    quick-start.md
    local-development.md
    local-testing.md
    troubleshooting-startup.md
  architecture/
    index.md
    system-overview.md
    solution-structure.md
    runtime-flows.md
    data-and-persistence.md
    infrastructure-and-deploy.md
  features/
    index.md
    agent-runtime/
      index.md
      turn-pipeline.md
      context-gating.md
      history-shaping.md
      scoped-retrieval.md
      model-routing.md
      response-enhancement.md
      learning-pipeline.md
      scheduler.md
      orchestration.md
      quality-gates.md
    channels/
      index.md
      channel-routing.md
      authentication.md
    ui/
      index.md
      chat.md
      diagnostics.md
      admin.md
      knowledge.md
      onboarding.md
    tools/
      index.md
      tool-governance.md
      browser-tool.md
  api/
    index.md
    gateway-api.md
    diagnostics-api.md
  configuration/
    index.md
    configuration-reference.md
    environment-variables.md
    appsettings-reference.md
  development/
    index.md
    build-and-test.md
    quality-gates.md
    docs-style-guide.md
  operations/
    index.md
    production-ops.md
    health-and-observability.md
  skills/
    index.md
    skill-format.md
    runtime-skills.md
  plans/
    index.md
    active/
      index.md
      ...
    archive/
      index.md
      ...
```

## 5) README Strategy

`README.md` becomes a concise entry portal and keeps only high-value overview content:

1. Product overview + value proposition (shortened).
2. Fast path: local run and test commands.
3. Direct links to:
   - docs home (`docs/index.md`)
   - getting started
   - configuration reference
   - API docs
   - architecture overview
4. Remove deep implementation duplication from README and point to canonical docs pages.

## 6) Migration Plan (Phased)

### Phase A: Inventory and Source-of-Truth Mapping

1. Build a docs inventory table for all `docs/**/*.md`:
   - file path
   - intended audience
   - status (`accurate`, `partial`, `stale`, `duplicate`)
   - canonical replacement target
2. Create code-to-doc ownership map using `src/` projects and key runtime surfaces.
3. Define canonical page for each domain capability.

### Phase B: Information Architecture Setup

1. Create the new folder hierarchy and `index.md` files.
2. Add section-level landing pages with short summaries and ordered “read next” links.
3. Move planning content into `docs/plans/active/` and `docs/plans/archive/`.

### Phase C: Content Reconciliation and Consolidation

1. Update architecture docs to match current runtime and project boundaries.
2. Consolidate duplicate feature pages into canonical pages.
3. Rewrite configuration docs as reference-first (single canonical config reference).
4. Split API docs into stable endpoint-focused references.
5. Update UI docs to reflect actual Blazor pages/routes/components.

### Phase D: README Refactor and Cross-Linking

1. Refactor `README.md` into concise portal format.
2. Ensure every docs page has:
   - “Related pages”
   - “Source references” (code paths)
   - backlinks to parent index
3. Add explicit deprecation notices + redirects (or replacement links) for moved pages.

### Phase E: Discoverability and Quality Gates

1. Add docs link-check and orphan-page checks to CI (or scripted local check first).
2. Add docs linting conventions (heading structure, relative links, code block language tags).
3. Validate no broken references from README and each `index.md`.

## 7) Deliverables

1. Updated `README.md` as portal.
2. New docs IA with `index.md` in every folder.
3. Consolidated feature and architecture docs with duplicates removed.
4. Active/archive plan taxonomy under `docs/plans/`.
5. `docs/development/docs-style-guide.md` containing writing and linking standards.
6. Optional scripted checker for links + required section presence.

## 8) Acceptance Criteria

1. **Accuracy**: Every major capability documented matches current code behavior/routes/config.
2. **Structure**: All doc files/folders use kebab-case; all folders contain `index.md`.
3. **Discoverability**: A new contributor can find setup, run, test, config, API, and architecture from `README.md` in <= 3 clicks.
4. **Deduplication**: No overlapping canonical feature docs for the same capability.
5. **Integrity**: No broken relative links from `README.md` or docs indexes.

## 9) Implementation Notes and Conventions

1. Keep markdown file names kebab-case only.
2. Prefer short pages with strong cross-links over very long omnibus pages.
3. Use repository-relative links consistently.
4. Keep roadmap/future work in `docs/plans/*`, not in implementation reference pages.
5. For each moved page, include replacement link in old location until cleanup pass is complete.

## 10) Execution Order (Recommended)

1. Create IA skeleton (`index.md` tree).
2. Reconcile architecture and configuration first (highest dependency for all other docs).
3. Reconcile API and feature docs.
4. Reconcile UI docs.
5. Refactor README.
6. Run docs QA checks and finalize redirects/deprecations.

## 11) Risks and Mitigations

1. **Risk**: Large move set causes link breakage.
   - **Mitigation**: staged migration + temporary compatibility stubs.
2. **Risk**: Ongoing code changes during rewrite.
   - **Mitigation**: freeze window per section and reconcile against latest main before merge.
3. **Risk**: Plans/docs mixing persists.
   - **Mitigation**: strict active/archive plan taxonomy and index curation.
