# Phase 16 Terminal Shared Runtime

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Create a reusable terminal-shared library that removes duplicated runtime code across Signal and Teams terminals, and move any equivalent duplication currently in Gateway into the same common location so behavior is consistent and easier to maintain.

## Scope
Refactor duplicated terminal and gateway helper logic into a new common project under `src/Common`, update terminal and gateway references, preserve behavior, and verify builds for impacted projects.

## In Scope
- Add a new common project for terminal/gateway shared helpers.
- Move shared health-response serialization used by gateway and terminals.
- Move repeated connection-string resolution and channel credential lookup helpers.
- Move repeated gateway `/v1/responses` extraction logic used by terminal clients.
- Update references and wiring in Signal terminal, Teams terminal, and Gateway where relevant.

## Out of Scope
- Functional redesign of channel protocols.
- Changing auth semantics or claims model.
- New transport features.

## Entry Criteria
- Existing terminal projects build and run in compose.
- Duplicate code hotspots identified and mapped.

## Exit Criteria
Refactor compiles for impacted projects, duplicated helper code is removed from terminal and gateway projects where practical, and docs/evidence are updated. See `exit-criteria.md`.

## Roles
- Owner: Coding agent
- Reviewer: Sub-agent plan reviewer
- Approver: Repository maintainer
