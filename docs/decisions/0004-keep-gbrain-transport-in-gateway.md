# ADR 0004: Keep GBrain transport in Gateway and Logic provider-agnostic

- Status: Accepted
- Date: 2026-07-13

## Context

While implementing the memory stack, the logs repeatedly called out a boundary risk: if GBrain-specific transport code leaked into `LeanKernel.Logic`, the common libraries would stop being reusable and the memory provider would become coupled to one transport choice.

This became an explicit user instruction during implementation, and the resulting changes reinforced that boundary.

## Decision

GBrain transport and integration code stays in `LeanKernel.Gateway`, while `LeanKernel.Logic` depends only on provider-agnostic abstractions.

Specifically:

- `LeanKernel.Logic` owns memory shaping, retrieval admission, and provider abstractions such as `IMemoryClient`.
- `LeanKernel.Gateway` owns concrete GBrain wiring, HTTP/MCP integration, and environment-specific memory transport.
- Logic emits scope-relative memory keys and page content; Gateway-specific clients prepend or translate storage scope as needed.

## Consequences

Positive:

- `LeanKernel.Logic` stays reusable and testable without a live GBrain dependency.
- Alternative memory backends can be introduced behind `IMemoryClient` without changing logic-layer code.
- Gateway remains the natural place for integration settings, auth handlers, and transport-specific concerns.

Tradeoffs:

- The abstraction boundary must carry enough information for storage without exposing transport details.
- Some debugging requires stepping across the Logic/Gateway boundary.
- Memory key semantics must remain carefully coordinated to avoid double-prefixing or scope drift.

## Evidence From Session Logs

- OpenCode session `ses_0a7da54c4ffeWVMS0xgNX63c3m`, `2026-07-12`, "Implement 5W1H memory logic PRD with phased commits"
  - User explicitly required that GBrain stay in `LeanKernel.Gateway` so `Common` libraries remain reusable.
  - Implementation summary confirmed that memory transport wiring stayed in Gateway and that `LeanKernel.Logic` was kept free of Gateway-specific GBrain types.
  - The session also documented that Logic emits scope-relative keys only, leaving outer storage scope composition to the transport layer.
- Copilot session `2e34b49f-0e5c-4dcb-9912-69fa3fe84acc`, `2026-07-12`, "Review PRD for Architecture and Implementation"
  - The PRD review reinforced the boundary that `LeanKernel.Logic` owns parsing, normalization, linking, and rendering, while Gateway owns HTTP transport, GBrain transport, runtime wiring, and optional batch triggers.
