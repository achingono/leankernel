# Documentation Implementation-State Refresh

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Refresh `README.md` and the canonical `docs/` pages so they accurately describe the repository's current implementation state, including active endpoints, feature set, configuration surface, quality workflows, and project/runtime boundaries.

## In Scope
- Update top-level documentation entry points (`README.md`, `docs/index.md`, category index pages).
- Update implementation-state pages for API, features, architecture, configuration, development, and operations.
- Align docs with current code anchors in `src/` and scripts in `scripts/quality/`.

## Out of Scope
- Introducing new runtime features beyond documentation-only clarifications.
- Large structural reorganization of docs folders.

## Entry Criteria
- Current implementation has been inspected from source code and startup mapping.
- Existing docs state drift has been identified.

## Exit Criteria
_What must be true before this phase closes. See `exit-criteria.md`._

## Roles
- Owner: OpenCode coding agent
- Reviewer: Independent review subagent/session
- Approver: Repository maintainer
