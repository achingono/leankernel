# Phase 4 UI Feature Documentation PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers, contributors, and operators who need implementation-accurate explanations of the Blazor UI surfaces.
- **Document type:** Explanation (Diátaxis)
- **Task goal:** Publish concise feature documentation for the five Phase 4 UI pages and link them from the feature index and README.
- **Plan review:** Reviewed by `gpt-5-mini`. Review outcome: proceed, explicitly document non-streaming chat delivery semantics, diagnostics API-key behavior, mock-backed admin limits, knowledge browse fallback limits, and onboarding page-write semantics.

## Problem statement

Phase 4 added a Blazor Server user interface in `LeanKernel.Gateway`, but the documentation set does not yet explain the shipped Chat, Diagnostics, Admin, Knowledge, and Onboarding pages at the same level of accuracy as the existing feature docs. Contributors can inspect the Razor components and services, but they do not have concise feature pages that explain what each surface does, how it is wired, what configuration affects it, and where the implementation currently stops.

## Scope

This task will:

1. Create `docs/features/ui-chat.md`.
2. Create `docs/features/ui-diagnostics.md`.
3. Create `docs/features/ui-admin.md`.
4. Create `docs/features/ui-knowledge.md`.
5. Create `docs/features/ui-onboarding.md`.
6. Update `docs/features/index.md` to link the new Phase 4 UI docs.
7. Update `README.md` with a brief `User Interface` section covering all five pages.

## Out of scope

- Changing Blazor UI behavior, routing, styling, or service contracts.
- Replacing older feature docs such as `blazor-chat-ui.md` or `identity-onboarding.md` unless directly required for navigation.
- Adding screenshots, diagrams, or images beyond text descriptions of what the user sees.
- Running `dotnet`, build, test, or Sonar steps in this environment.

## Documentation approach

### Diátaxis quadrant

Each requested file will be an **Explanation** document. The docs will help maintainers and operators understand the current UI behavior, its collaborators, degraded paths, and integration boundaries without reading every component and service first.

### Target audience and goal

- **Audience:** maintainers, contributors, and technical operators.
- **Goal:** understand what each Phase 4 page does today, how it works, what it depends on, and which API or service layer it uses.

### Style and structure

- Match the concise feature-doc tone used in `docs/features/blazor-chat-ui.md`, `diagnostics.md`, and `identity-onboarding.md`.
- Use the requested sections where applicable: `Overview`, `How It Works`, `Configuration`, `API Endpoints`, `Screenshots / Examples`, and `Related documentation`.
- Keep docs scannable and implementation-accurate.
- Use explicit wording for degraded or preview behavior instead of roadmap language.

## Source files

The docs must stay aligned to these implementation sources:

- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/Components/Layout/NavMenu.razor`
- `src/LeanKernel.Gateway/Components/Pages/Chat.razor`
- `src/LeanKernel.Gateway/Components/Shared/SessionList.razor`
- `src/LeanKernel.Gateway/Components/Shared/ChatMessage.razor`
- `src/LeanKernel.Gateway/Services/ChatService.cs`
- `src/LeanKernel.Gateway/wwwroot/js/chat.js`
- `src/LeanKernel.Gateway/Components/Pages/Diagnostics.razor`
- `src/LeanKernel.Gateway/Services/DiagnosticsService.cs`
- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/Components/Pages/Admin.razor`
- `src/LeanKernel.Gateway/Services/AdminService.cs`
- `src/LeanKernel.Gateway/Components/Pages/Knowledge.razor`
- `src/LeanKernel.Gateway/Services/KnowledgeUiService.cs`
- `src/LeanKernel.Gateway/Components/Pages/Onboarding.razor`
- `src/LeanKernel.Gateway/Services/OnboardingService.cs`
- `src/LeanKernel.Abstractions/Models/ContextDiagnosticsResponse.cs`
- `src/LeanKernel.Abstractions/Models/RoutingDecision.cs`
- `src/LeanKernel.Abstractions/Models/QualityGateResult.cs`
- `src/LeanKernel.Abstractions/Models/ShadowRoutingResult.cs`
- `docs/features/blazor-chat-ui.md`
- `docs/features/identity-onboarding.md`
- `docs/features/index.md`
- `README.md`

## Accuracy constraints

The new docs must reflect these implementation details:

- Chat is an `InteractiveServer` Blazor page at `/`, `/chat`, and `/chat/{sessionId}` with local storage keys `leankernel.chat.owner-id` and `leankernel.chat.sessions`.
- Chat supports new session creation, persisted session resume, pending user messages, auto-scroll, and Enter-to-send with Shift+Enter newline.
- Chat does **not** render token-by-token streaming in the current UI; it awaits `IAgentRuntime.RunTurnAsync` and reloads persisted history when the turn completes.
- Chat compaction badges are best-effort because compaction markers are matched by compacted content, not by turn id.
- Diagnostics calls `/api/diagnostics/{sessionId}`, `/context`, `/budget`, and `/history` in parallel, then merges routing, quality-gate, and shadow-routing payloads from raw diagnostic entries.
- Diagnostics endpoints require `X-Api-Key` when Gateway API-key auth is configured.
- Admin is a mock-backed, non-persistent preview. Provider refresh and tool toggles mutate in-memory `AdminService` state only, and routing/spend/job data are preview data.
- Knowledge search uses a 300 ms debounce.
- Knowledge browse uses GBrain `list_pages` when available, otherwise falls back to pages discovered in the current browser session cache.
- Knowledge detail enrichment uses GBrain `get_page` when available, but reads and writes still flow through `IKnowledgeService`.
- Onboarding is a five-step wizard (`Welcome`, `Identity`, `Knowledge Domains`, `Goals`, `Complete`) that reuses the browser owner id from chat.
- Onboarding writes two fixed knowledge pages: `wiki/identity/user-profile` and `wiki/identity/user-goals`.
- Onboarding gap guidance is advisory and filtered to supported identity fields.

## Deliverables

### New feature docs

- `docs/features/ui-chat.md`
- `docs/features/ui-diagnostics.md`
- `docs/features/ui-admin.md`
- `docs/features/ui-knowledge.md`
- `docs/features/ui-onboarding.md`

### Updated navigation docs

- `docs/features/index.md`
- `README.md`

## Validation plan

1. Verify every statement against the current Razor components, services, and endpoint definitions.
2. Check Markdown headings, relative links, and list formatting.
3. Review the final diff to ensure the docs stay concise and do not claim unimplemented behavior.
4. Do not run `dotnet` or Sonar because the user explicitly stated `dotnet` is unavailable and this task is documentation-only.

## Acceptance criteria

- All five requested UI feature docs exist at the specified paths.
- Each doc uses the requested scannable sections and remains implementation-accurate.
- `docs/features/index.md` links to all five new docs.
- `README.md` contains a brief `User Interface` section that mentions Chat, Diagnostics, Admin, Knowledge, and Onboarding.
- The reviewed PRD is saved under `docs/plans/` before the feature documentation edits.
- The final report clearly states that validation was limited to source inspection and documentation review because `dotnet` was unavailable.
