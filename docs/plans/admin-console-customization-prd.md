# PRD: Admin Console Customization and Routing Management

## Overview

Improve LeanKernel's admin console so operators can safely customize runtime behavior, maintain LiteLLM routing without hand-editing YAML, and complete onboarding with a clearer, more delightful setup flow.

This PRD is grounded in a live Playwright review of the running app at `http://localhost:5080`, plus code review of the current Blazor pages, config APIs, onboarding flow, runtime config store, and LiteLLM tooling.

## Problem Statement

The current admin console exposes only a narrow, mostly read-only slice of the system's runtime configuration. Operators can inspect some values, but they cannot confidently manage all persisted settings, understand config provenance, or maintain the LiteLLM routing source file through the UI.

The onboarding flow also behaves more like a raw bootstrap form than a guided setup experience. It validates core dependencies, but it does not communicate progress, tradeoffs, or next actions with enough clarity to feel polished.

## Goals

- Expose the full supported runtime configuration surface with explicit mutability and provenance.
- Prevent partial config editors from silently resetting hidden settings.
- Replace hand-maintained LiteLLM route editing with a purpose-built admin workspace.
- Add routing validation, simulation, and diff preview before writes.
- Make onboarding feel guided, trustworthy, and rewarding rather than form-heavy.
- Preserve the current file-backed architecture and YAML-first LiteLLM contract.

## Non-Goals (v1)

- Replacing file-backed runtime config with a database-backed configuration service.
- Editing provider secrets directly in the UI instead of through environment variables or secret stores.
- Multi-user RBAC beyond the current admin model.
- Replacing the LiteLLM source format with a different config schema.

## Current-State Review

### Live UI Findings

- The `Settings` page is an inspection surface, not a management surface.
- The `Routing` page is an observability dashboard only; it has no authoring workflow for `config/litellm/config.yaml`.
- Completed onboarding collapses into a minimal success card with no re-entry, summary, or guided next steps.
- Narrow-view settings rows previously compressed inputs awkwardly; that baseline issue has been fixed in the UI layer as part of this review.

### Runtime Settings Coverage Matrix

| Config Area | Present in `data/runtime-settings.json` | `/settings` UI | `/onboarding` UI | Current Persistence Safety | Gap |
| --- | --- | --- | --- | --- | --- |
| LiteLLM | Yes | Read-only | Editable | Round-tripped | No edit path after onboarding |
| Qdrant | Yes | Read-only | Editable | Round-tripped | No edit path after onboarding |
| Signal core fields | Yes | Read-only | Editable | Partial | `allowedSenders` and `daemonBaseUrl` are missing from UI |
| Unstructured | Yes | No | No | Not round-tripped | Hidden runtime dependency with no admin surface |
| Wiki | Yes | Read-only | Editable | Round-tripped | No post-bootstrap editing |
| Agents | Yes | No | Indirect via separate AGENTS flow | Not round-tripped | Path/config ownership unclear |
| Knowledge | Yes | No | No | Round-tripped | Collection, tags, and scope rules are hidden |
| Context | Yes | Read-only | No | Round-tripped | Operators cannot tune retrieval weights in onboarding |
| Scheduler | Yes | Read-only | Editable | Round-tripped | No operational hints or cron validation |
| Auth | Yes | No | Passcode only | Round-tripped | No mode, OIDC, or rate-limit controls |
| Routing | Yes | Summary only | No | Not round-tripped | High-risk gap for future settings editor |
| Engagement | Yes | No | Separate AGENTS step only | Not round-tripped | Runtime JSON and AGENTS.md ownership are split |
| Channel secrets/tokens | Yes | No | No | Not round-tripped | Hidden and easy to mis-handle |

### Structural Gaps in the Current Implementation

| Surface | Current Behavior | Consequence |
| --- | --- | --- |
| `src/LeanKernel.Host/Components/Pages/Settings.razor` | Shows a subset of config as read-only inputs | Creates false expectation that settings are manageable from the UI |
| `src/LeanKernel.Host/Controllers/ConfigController.cs` | Returns only LiteLLM, Qdrant, Signal, Wiki, Context, Scheduler | API coverage does not match `LeanKernelConfig` |
| `src/LeanKernel.Host/Services/OnboardingOrchestrator.cs` | Saves only LiteLLM, Qdrant, Signal, Wiki, Scheduler | Onboarding edits cannot cover the full runtime surface |
| `src/LeanKernel.Host/Services/RuntimeLeanKernelConfigStore.cs` | Clone/save path omits `Unstructured`, `Agents`, `Routing`, `Engagement`, `Signal.DaemonBaseUrl`, and channel token fields | Any future editor built on this store risks resetting hidden values to defaults |
| `src/LeanKernel.Host/Components/Pages/Routing.razor` | Observability cards only | Operators still have to maintain YAML by hand |
| `config/litellm/config.yaml` | Human-authored route graph with repeated chains and separated alias/fallback sections | Maintenance is error-prone and hard to validate visually |
| `scripts/sync_litellm_model_limits.py` | Sync exists only as a script | Limit drift remains invisible to UI operators |

### LiteLLM Maintenance Pain Points

- Tier routes are edited as repeated provider/model/key tuples instead of as an ordered route graph.
- Alias fallback behavior is separated from the tier definitions that motivate it.
- Environment-key readiness is invisible while editing.
- Embedding routes live alongside chat routes without a distinct UX.
- Syncing live model limits is a script-only workflow disconnected from the admin experience.
- It is difficult to answer simple questions such as "what changed", "what is disabled", and "which env slot powers this route".

## User Stories

- As an admin, I can edit all supported runtime settings from one place without accidentally resetting unrelated fields.
- As an operator, I can see which settings are runtime-live, restart-required, secret-backed, or derived from another file.
- As a model-routing maintainer, I can reorder routes, manage aliases, inspect key readiness, and preview YAML diffs before saving.
- As a maintainer, I can simulate how a sample request would route before enabling changes.
- As a first-time user, I can complete onboarding as a guided journey with clear progress, validation feedback, and next steps.
- As a returning admin, I can revisit onboarding decisions after setup instead of losing the original context.

## Functional Requirements

### FR-1 Full-Fidelity Runtime Settings Editor

- Add a write-capable admin settings workspace backed by a config metadata API.
- Group settings into clear sections:
  - Connectivity
  - Retrieval
  - Routing
  - Authentication
  - Channels
  - Scheduler
  - Agent behavior
- Every field must expose metadata:
  - source of truth
  - last saved value
  - secret/reference status
  - restart required vs. hot reload
  - validation state

### FR-2 Persistence-Safe Config Round-Trip

- Expand the runtime config store so it can round-trip every supported `LeanKernelConfig` section without loss.
- Any unsupported field must be explicitly marked unsupported rather than silently omitted.
- Saving must be diff-aware and atomic.
- Operators must see a change review before persistence.

### FR-3 LiteLLM Routing Workspace

- Add a dedicated routing management view for the source file at `config/litellm/config.yaml`.
- The routing workspace must edit the source spec, not the rendered runtime output.
- Support authoring for:
  - providers
  - provider key slots
  - model catalog entries
  - route lanes (`small`, `medium`, `large`, embeddings)
  - alias mappings
  - route and alias fallbacks
  - router policy settings

### FR-4 Routing Validation and Simulation

- Validate references before save:
  - provider exists
  - model exists for provider
  - key slot exists
  - alias points to valid route
  - fallback targets are valid
- Surface duplicate ordering and unreachable candidates as warnings.
- Provide a dry-run simulation panel where an operator can enter:
  - expected complexity tier
  - context size
  - priority
  - provider availability assumptions
- Show the predicted route chain and fallback path.

### FR-5 Key Readiness and Drift Visibility

- Show environment-backed key slots as references only; do not expose raw secrets.
- Surface whether each slot is configured, disabled, or missing.
- Integrate model-limit drift visibility from `scripts/sync_litellm_model_limits.py` into the UI.
- Offer "preview drift" and "apply synced limits" actions with a YAML diff preview.

### FR-6 Guided Onboarding Stepper

- Replace the current card wall with a stepper-based flow:
  1. Deployment profile / starter preset
  2. LiteLLM connectivity
  3. Retrieval and document storage
  4. Channels and optional integrations
  5. Agent behavior and rules
  6. Authentication and security
  7. Validation and launch
- Persist progress and return the user to the last incomplete step.
- Keep the current validation checks, but attach them to the relevant step.

### FR-7 Post-Completion Re-entry

- Preserve a setup summary after completion.
- Add explicit actions:
  - Reopen onboarding
  - Review saved settings
  - Launch first chat
  - Open routing workspace
- Show what was configured, what was skipped, and what still needs attention.

## UI Requirements

### Runtime Settings Workspace

| Region | Purpose |
| --- | --- |
| Left rail | Section navigation, search, unsaved-changes indicator |
| Main form | Field groups with descriptions, validation, and dependency hints |
| Right rail | Provenance, restart impact, last-saved diff, related docs |
| Footer bar | Save draft, validate, discard, export diff |

Key interaction patterns:

- Treat secrets as references and health indicators, not raw values.
- Use badges such as `runtime-live`, `restart-required`, `env-backed`, and `advanced`.
- Provide inline examples for cron, URL, and collection-name fields.
- Show "why this matters" helper copy for expert-only settings.

### LiteLLM Routing Workspace

| Pane | Purpose |
| --- | --- |
| Provider inventory | Providers, key slots, env readiness, enabled state |
| Model catalog | Provider models, mode, limits, and feature support |
| Route canvas | Ordered lane editor for `small`, `medium`, `large`, and embedding routes |
| Inspector | Entry details, order, fallback behavior, duplicate detection |
| Preview drawer | YAML diff, validation results, simulation output |

Required UX behaviors:

- Route lanes must support drag-and-drop reordering.
- Embedding lanes must be visually separate from chat lanes.
- Alias mappings should be edited as chips or rows linked directly to target routes.
- Route and alias fallbacks must be visible in the same workspace, not hidden in a separate file region.
- Save should present a canonical YAML preview with stable ordering.

### Onboarding Delight Enhancements

| Moment | Enhancement |
| --- | --- |
| Entry | Offer starter profiles such as local-only, cloud-routed, and hybrid |
| Each step | Show progress, estimated time, and dependency readiness |
| Validation | Attach fixes and retry actions directly to each failed check |
| Agent rules | Preview what the chosen preset changes before applying it |
| Completion | Replace the bare success card with a launch checklist and next actions |
| Return visits | Show "last configured" summary and what changed since onboarding |

Presentation guidance:

- Use a calm guided rhythm instead of one large wall of forms.
- Prefer progressive disclosure for advanced settings.
- Reward successful validation with small celebratory state changes, not just status text.
- Make optional steps skippable without making them feel broken or second-class.

## API and Backend Requirements

| Endpoint / Service | Requirement |
| --- | --- |
| `GET /api/config` replacement | Return full config metadata and values safe for UI display |
| Runtime config store | Round-trip all supported runtime sections safely |
| Routing config API | Parse, validate, diff, and save `config/litellm/config.yaml` |
| Drift API | Expose model-limit drift preview from live metadata |
| Simulation API | Return predicted route chain for sample inputs |
| Onboarding state API | Track active step, skipped steps, and completion summary |

## Acceptance Criteria

- AC-1: An admin can edit every supported runtime setting section without hidden-field loss on save.
- AC-2: The settings UI explicitly labels unsupported or env-only fields instead of omitting them silently.
- AC-3: A maintainer can reorder routes, change aliases, and preview the exact YAML diff before persisting `config/litellm/config.yaml`.
- AC-4: The routing workspace surfaces env readiness and validation errors before save.
- AC-5: Model-limit drift can be previewed from the UI without running a shell script manually.
- AC-6: Onboarding exposes progress, step-local validation, and post-completion next actions.
- AC-7: Returning admins can re-enter onboarding decisions after initial completion.
- AC-8: No runtime configuration save path drops `Routing`, `Engagement`, `Unstructured`, `Agents`, or `Signal.DaemonBaseUrl` values.

## Dependencies

- Runtime config round-trip work in `src/LeanKernel.Host/Services/RuntimeLeanKernelConfigStore.cs`
- Expanded config API surface beyond `src/LeanKernel.Host/Controllers/ConfigController.cs`
- LiteLLM source-schema understanding in `config/litellm/config.yaml` and `config/litellm/render_litellm_config.py`
- Drift metadata from `scripts/sync_litellm_model_limits.py`
- Onboarding orchestration changes in `src/LeanKernel.Host/Services/OnboardingOrchestrator.cs`

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Partial config editors reset hidden values | Make full round-trip safety a prerequisite before write-capable settings ship |
| Routing UI drifts from YAML schema | Validate against the source schema and keep canonical serialization in one backend path |
| Secret leakage through admin UI | Continue env-backed secret references and show status only |
| Onboarding becomes too long | Use presets, optional-step skipping, and progressive disclosure |
| Advanced routing edits overwhelm casual operators | Keep a basic mode with guided defaults and an advanced mode for full graph editing |

## Implementation Sequence

1. Fix runtime config round-trip coverage and expose config metadata/provenance.
2. Convert `Settings` from read-only inspection to safe edit + review workflow.
3. Add backend parsing, validation, and diff APIs for `config/litellm/config.yaml`.
4. Ship the routing workspace with simulation, env readiness, and drift preview.
5. Rebuild onboarding as a guided stepper with re-entry and completion summary.

## Sprint-Ready Engineering Tickets

- [ ] `ADM-01` Expand `RuntimeLeanKernelConfigStore` so it round-trips every supported `LeanKernelConfig` section without data loss.
- [ ] `ADM-02` Replace the current subset config API with a metadata-rich admin config contract covering provenance, mutability, and restart requirements.
- [ ] `ADM-03` Build a write-capable settings workspace with diff preview, validation, and explicit env-backed secret handling.
- [ ] `ADM-04` Implement a routing config backend that parses, validates, diffs, and saves `config/litellm/config.yaml` through one canonical serializer.
- [ ] `ADM-05` Build the LiteLLM routing workspace with provider inventory, model catalog, route lanes, alias management, and YAML diff preview.
- [ ] `ADM-06` Integrate model-limit drift preview and apply workflows using the existing sync logic.
- [ ] `ADM-07` Redesign onboarding as a stepper with starter profiles, per-step validation, progress persistence, and a completion hub.
- [ ] `ADM-08` Add post-onboarding re-entry points from the dashboard, settings, and completion view.
