# Phase 17 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Existing Playwright test harness | `test/LeanKernel.Tests.Playwright/PlaywrightTests.cs` | Engineering |
| Docker topology and service ports | `docker-compose.yml` | Engineering |
| Identity and memory runtime behavior | `src/Services/LeanKernel.Gateway/Providers/TenantResolutionMiddleware.cs`, `src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs` | Engineering |
| Memory transport contracts | `src/Services/LeanKernel.Gateway/Memory/GBrainMcpClient.cs` | Engineering |

## Optional Inputs
- Existing local `.env` values for docker port overrides.

## Input Validation Checklist
- [x] All required inputs are current (not from a superseded version)
- [x] No required input is missing or in draft state
