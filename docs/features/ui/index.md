# UI Features

Blazor UI capability docs for current Gateway pages.

## Pages

- [Chat UI](../ui-chat.md) - sessions list, composer, and turn history
- [Diagnostics UI](../ui-diagnostics.md) - context/budget/history diagnostics explorer
- [Admin UI](../ui-admin.md) - provider health, routing view, tool governance toggles, spend, scheduler
- [Knowledge UI](../ui-knowledge.md) - wiki search/browse/edit plus document library and ingestion
- [Onboarding UI](../ui-onboarding.md) - guided setup wizard (nav label: Setup)

## Route Reference

- `/` and `/chat` and `/chat/{sessionId}`
- `/diagnostics`
- `/admin`
- `/knowledge`
- `/onboarding` (shown as "Setup" in nav)

## Related Pages

- [Features index](../index.md)
- [Architecture](../../architecture/index.md)

## Source References

- `src/LeanKernel.Gateway/Components/Pages/Chat.razor`
- `src/LeanKernel.Gateway/Components/Pages/Diagnostics.razor`
- `src/LeanKernel.Gateway/Components/Pages/Admin.razor`
- `src/LeanKernel.Gateway/Components/Pages/Knowledge.razor`
- `src/LeanKernel.Gateway/Components/Pages/Onboarding.razor`
