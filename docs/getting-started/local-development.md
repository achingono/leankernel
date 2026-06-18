# Local Development

LeanKernel is a modular monolith. Most local work runs from `src/LeanKernel.Gateway`.

## Typical Development Loop

1. Build the solution.
2. Run Gateway on `http://127.0.0.1:5080`.
3. Use the Blazor UI (`/chat`, `/admin`, `/diagnostics`, `/knowledge`, `/onboarding`) and/or API endpoints.

## Development Configuration

`appsettings.Development.json` overrides hostnames and connection strings for local services.

- LiteLLM: `http://localhost:4000`
- GBrain: `http://localhost:8789`
- Signal daemon: `http://localhost:8080`
- Database: local Postgres connection string override

## Related Pages

- [Configuration reference](../configuration/index.md)
- [API docs](../api/index.md)
- [Architecture overview](../architecture/index.md)

## Source References

- `src/LeanKernel.Gateway/appsettings.Development.json`
- `src/LeanKernel.Gateway/Components/Pages/Chat.razor`
- `src/LeanKernel.Gateway/Components/Pages/Admin.razor`
- `src/LeanKernel.Gateway/Components/Pages/Diagnostics.razor`
- `src/LeanKernel.Gateway/Components/Pages/Knowledge.razor`
- `src/LeanKernel.Gateway/Components/Pages/Onboarding.razor`
