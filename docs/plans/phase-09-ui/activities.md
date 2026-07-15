# Phase 09 Activities

## Step-By-Step Activities
1. Stand up the Blazor Server app shell in `LeanKernel.Gateway`: layout, routing, navigation, and per-user session continuity, integrated with the existing auth/partitioning.
2. Implement the Chat UI: sessions list, composer, turn/streaming state, and history rendering over the runtime turn surface, using a thin `ChatService`.
3. Implement the Knowledge UI: wiki search/browse/edit plus document library and ingestion status, using thin `KnowledgeUiService`/`DocumentUiService`.
4. Implement the Onboarding wizard over the Phase 07 onboarding intelligence, guiding identity setup and reflecting detected gaps.
5. Implement the Diagnostics explorer over the Phase 08 API: per-turn context/budget/history/retrieval views.
6. Implement the Admin console: provider health, routing view, tool governance toggles, spend view, and scheduler controls, using a thin `AdminService`.
7. Ensure every UI surface is partition-aware and enforces identity/authorization consistent with the gateway.
8. Replace the Development-only DevUI mapping with the real UI (retain DevUI only where useful for local dev).
9. Add component tests and Playwright end-to-end tests for chat, knowledge, onboarding, diagnostics, and admin flows.
10. Document the UI surfaces in `docs/features/` and update navigation/getting-started docs.

## Review Focus
- UI services stay thin and do not duplicate runtime logic.
- Every surface enforces identity/partitioning and authorization.
- No cross-tenant/user data leakage in lists, search, or diagnostics.
- Accessible, consistent baseline; graceful handling of unavailable backends.
- Playwright tests target the primary flows deterministically.
