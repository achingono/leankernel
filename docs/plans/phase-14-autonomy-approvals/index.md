# Phase 14 Autonomy And Approval Policy Engine

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Make the assistant safe to act on the user's behalf by introducing an autonomy/approval policy engine and a durable action audit log. Any side-effecting action — sending email, modifying calendar events, running connector writes, executing risky tools — is classified by risk and either auto-approved, deferred for explicit human confirmation, or blocked, according to per-person and per-tenant policy. Every executed action is recorded in a tamper-evident audit trail. This is the trust backbone for Phases 11–13 and any future action-taking feature.

## Scope
This phase delivers the policy classification, approval workflow (request, notify, confirm/deny, expire), enforcement interception point, and the action audit log. It integrates with tool governance, channels (to request approval), and diagnostics. It does not implement specific actions (those live in their feature phases); it governs them.

## In Scope
- An action risk model: classify tool/connector/feature actions by side-effect severity (read/reversible/irreversible/financial) and required autonomy level.
- A policy engine evaluating per-person and per-tenant policy to decide auto-approve, require-approval, or block for each action, with sensible safe defaults.
- An approval workflow: create an approval request, notify the user on their preferred/available channel, capture confirm/deny, and expire unanswered requests safely (default deny).
- A single enforcement interception point in the runtime/tool-execution path so no governed action bypasses policy.
- A durable, tamper-evident action audit log recording who/what/when/decision/outcome, person- and tenant-scoped, queryable via the diagnostics API (Phase 08).
- Configuration for default autonomy levels, per-action overrides, approval timeouts, and quiet-hours behavior; startup validation.
- Integration hooks so Phases 11–13 route writes through this engine and fail safe when it is disabled.
- Tests for classification, policy decisions, approval lifecycle/expiry, enforcement (no bypass), audit completeness, and isolation.

## Out of Scope
- The specific side-effecting features (email/calendar/tasks/connectors) — they consume this engine.
- A full policy-authoring UI beyond what Phase 09 admin surfaces.

## Entry Criteria
- Tool runtime + governance (Phase 01) provides the execution interception surface.
- Channels (Phase 06) can deliver approval prompts and capture responses.
- Diagnostics persistence (Phase 08) can back the audit log, or an equivalent durable store exists.
- Person/tenant scoping (Phase 10) is available for per-person policy.

## Exit Criteria
Every side-effecting action is classified and gated by policy — auto-approved, explicitly confirmed, or blocked — unanswered approvals default to deny, no governed action bypasses the engine, and all actions are recorded in a durable audit trail. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
