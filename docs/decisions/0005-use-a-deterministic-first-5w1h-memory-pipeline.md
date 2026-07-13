# ADR 0005: Use a deterministic-first 5W1H memory pipeline with bounded small-model refinement

- Status: Accepted
- Date: 2026-07-13

## Context

The source repository already had reusable 5W1H page-shaping logic, but it lived inside scheduler-oriented code. The rebuild needed that capability inside `LeanKernel.Logic`, without importing scheduler orchestration.

The planning sessions then extended that requirement: memory pages should not only normalize 5W1H fields, but also identify dominant dimensions, organize pages by those dimensions, and form a navigable graph. At the same time, the logs repeatedly rejected an LLM-only design because the pipeline still needed to work when the small model was disabled or failed.

## Decision

LeanKernel will implement the memory pipeline in `LeanKernel.Logic` as deterministic-first 5W1H processing with optional, bounded small-model refinement.

The logic layer owns:

- fact extraction from conversations
- page parsing and rendering
- 5W1H normalization
- missing-field tracking
- dimension classification and ranking
- key generation by primary dimension
- deterministic link generation
- optional small-model refinement for ambiguous dimensions, bounded graph edges, and fill-missing-only repair

The operating rules are:

- deterministic logic runs first
- small-model passes are optional and separately configured
- model output must be structured and evidence-grounded
- model-assisted repair cannot overwrite populated fields
- deterministic fallback remains mandatory on timeout, parse failure, or disabled configuration

## Consequences

Positive:

- The system stays inspectable and usable without a model-dependent happy path.
- Memory pages remain human-readable and machine-derivable.
- The graph and dimension system can improve recall without making retrieval opaque.

Tradeoffs:

- The pipeline is more complex than simple text memory storage.
- Additional tests and observability are required to keep deterministic and LLM-assisted branches aligned.
- Small-model prompts, budgets, and validation rules become part of the architecture.

## Evidence From Session Logs

- OpenCode session `ses_0a81f451effevpB4Agt5S5J2iJ`, `2026-07-12`, "PRD for 5W1H memory logic adaptation"
  - Chose to import reusable 5W1H logic from the source repo into `LeanKernel.Logic` without dragging scheduler behavior into the new runtime.
  - Expanded the design to include key-dimension identification, canonical organization by primary dimension, deterministic cross links, and page graphing.
  - Explicitly added small-LLM reasoning for ambiguous dimension extraction and graph building, while keeping deterministic fallback mandatory.
- Copilot session `2e34b49f-0e5c-4dcb-9912-69fa3fe84acc`, `2026-07-12`, "Review PRD for Architecture and Implementation"
  - Reinforced the architectural split: Logic owns parsing, normalization, dimensions, linking, and rendering; Gateway owns transport and runtime wiring.
  - Reviewed and strengthened the deterministic-first plus bounded-small-model design.
- OpenCode session `ses_0a7da54c4ffeWVMS0xgNX63c3m`, `2026-07-12`, "Implement 5W1H memory logic PRD with phased commits"
  - Implementation summary confirmed the landed service set in `LeanKernel.Logic`, including parser, renderer, normalizer, dimension classifier, linker, graph reasoner, repair service, and fact extraction service.
