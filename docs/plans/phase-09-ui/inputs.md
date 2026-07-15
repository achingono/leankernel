# Phase 09 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Runtime turn surface | Gateway `/v1/responses` + Phase 03 pipeline | Rebuild maintainer |
| Diagnostics API | Phase 08 diagnostics query API | Rebuild maintainer |
| Knowledge/document services | Phase 05 ingestion + `GBrainKnowledgeService` | Rebuild maintainer |
| Onboarding intelligence | Phase 07 onboarding gap/directive services | Rebuild maintainer |
| Identity/auth + partitioning | `src/Services/LeanKernel.Gateway/Programs.cs`, `Providers/RequestContextPermit.cs` | Rebuild maintainer |
| Source UI pages | `~/source/repos/leankernel/src/LeanKernel.Gateway/Components/Pages/*.razor` | Reviewer |
| Source UI services | `~/source/repos/leankernel/src/LeanKernel.Gateway/Services/{ChatService,DiagnosticsService,AdminService,KnowledgeUiService,DocumentUiService,OnboardingService}.cs` | Reviewer |
| Playwright harness | `test/LeanKernel.Tests.Playwright` | Rebuild maintainer |

## Optional Inputs
- Source UI PRDs/screenshots: `phase-4-*`, `blazor-chat-ui.md`, `ui/*`, `knowledge-browser-ui-prd.md`, `onboarding-*-prd.md`, `docs/plans/screenshot_*.png`.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Backing APIs/services for each UI area confirmed available or sequenced
