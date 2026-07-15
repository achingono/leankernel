# Phase 09 Blazor User Interface

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Deliver the operator- and user-facing web UI for the rebuild as a Blazor Server surface hosted by the gateway: a chat experience, a diagnostics explorer, an admin/governance console, a knowledge browser, and an onboarding wizard. This ports the source repo's `LeanKernel.Gateway` Blazor Components/Pages and their backing UI services, consuming the runtime, diagnostics, knowledge, and onboarding capabilities delivered in earlier phases.

## Scope
This phase is the presentation layer only; it consumes existing runtime/diagnostics/knowledge/onboarding APIs and services rather than reimplementing them. It replaces the current Development-only DevUI with real product UI. It does not add new runtime behavior beyond thin UI-backing services.

## In Scope
- App shell, routing, navigation, and session continuity for a Blazor Server app hosted in `LeanKernel.Gateway`.
- Chat UI: sessions list, composer, streaming/turn state, and history rendering over the runtime turn surface.
- Diagnostics UI: context/budget/history/retrieval explorer over the Phase 08 diagnostics API.
- Admin UI: provider health, routing view, tool governance toggles, spend view, and scheduler controls.
- Knowledge UI: wiki search/browse/edit plus document library and ingestion status.
- Onboarding UI: guided setup wizard over the Phase 07 onboarding intelligence.
- Thin UI-backing services (chat, diagnostics, admin, knowledge/document, onboarding) that call existing runtime/gateway services.
- Authentication/authorization integration with the gateway's existing identity/partitioning and API protection.
- Component and Playwright end-to-end tests for the primary flows.

## Out of Scope
- New runtime, tool, channel, learning, scheduler, or diagnostics behavior (delivered in Phases 03-08).
- A specific design-system migration beyond a consistent, accessible baseline (can be a follow-up).
- Mobile-native clients.

## Entry Criteria
- Runtime turn surface, diagnostics API (Phase 08), knowledge/document services (Phase 05), and onboarding intelligence (Phase 07) are available. UI can be built incrementally as those land; chat + knowledge can precede diagnostics/admin.
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Gateway/Components/Pages/{Chat,Diagnostics,Admin,Knowledge,Onboarding}.razor` and `Services/{ChatService,DiagnosticsService,AdminService,KnowledgeUiService,DocumentUiService,OnboardingService}.cs`, plus `Components/*` shell and `Program.cs` Blazor wiring.

## Exit Criteria
Authenticated users can chat, browse/edit knowledge and documents, complete onboarding, and operators can inspect diagnostics and govern the runtime through the admin console — all partition-aware and covered by end-to-end tests. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
