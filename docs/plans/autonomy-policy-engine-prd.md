# PRD: Autonomy Policy Engine and Per-Tool Approval Gates

## Overview

Introduce an autonomy policy engine that controls whether agent actions are suggest-only, approval-gated, or auto-executed, with policy scope at global, route, tool-category, and individual tool levels.

## Problem Statement

Current agent workflows can feel opaque and risky when actions are executed without explicit user control. Teams need a progressive trust model that enables safe adoption from pilot to production.

## Goals

- Provide explicit control over agent action execution.
- Support progressive autonomy levels without code changes.
- Require approvals for high-risk tools/actions.
- Preserve auditability of decisions, approvals, and overrides.

## Non-Goals (v1)

- Multi-tenant policy inheritance across organizations.
- Fine-grained per-field approval semantics inside a single tool input.
- Natural-language policy authoring.

## User Stories

- As an admin, I can set the default autonomy mode for all agents.
- As an operator, I can require approval for specific tools (for example file writes, outbound sends, destructive operations).
- As a reviewer, I can approve or deny queued actions with context.
- As a compliance owner, I can audit every action and approval event.

## Autonomy Model

| Mode | Behavior |
| ---- | -------- |
| `suggest_only` | Agent proposes tool call; no execution allowed |
| `approval_required` | Agent proposes tool call; execution only after human approval |
| `auto_execute` | Agent executes tool call immediately, unless overridden by stricter policy |

Policy precedence (highest first):

1. Per-tool rule
2. Tool-category rule
3. Agent/route rule
4. Global default

Most restrictive rule wins.

## Functional Requirements

### FR-1 Policy Configuration

- Persist policy configuration in app settings and runtime state.
- Support scopes: global, route, category, tool.
- Include allow/deny override controls.

### FR-2 Execution Decision Engine

- Evaluate tool call against effective policy before execution.
- Return deterministic decision: `deny`, `queue_for_approval`, or `execute`.
- Include decision reason code in response/log.

### FR-3 Approval Queue

- Queue approval-required actions with full context.
- Expose reviewer actions: approve, deny, expire.
- Enforce expiration timeout and default deny on timeout.

### FR-4 Reviewer Experience

- Show risk level, tool name, arguments diff/preview, and request origin.
- Require reviewer comment for deny action.
- Record reviewer identity and timestamp.

### FR-5 Safe Fallback Behavior

- If policy lookup fails, default to `approval_required`.
- If approval subsystem is unavailable, block high-risk tools and log incident.

### FR-6 Audit Trail

- Record policy resolution, queued approvals, reviewer decisions, final execution outcome.
- Ensure immutable log format suitable for export.

## Non-Functional Requirements

- Decision latency p95 <= 50ms in-process.
- Approval queue availability >= 99.9%.
- No tool execution without explicit decision event.
- Full traceability via correlation ID.

## Architecture

| Component | Responsibility |
| --------- | -------------- |
| `AutonomyPolicyResolver` | Builds effective policy for tool call |
| `ToolExecutionGuard` | Enforces decision before dispatch |
| `ApprovalQueueStore` | Stores pending approvals and status |
| `ApprovalService` | Reviewer actions and lifecycle |
| `AutonomyAuditLogger` | Structured audit records |

## Data Model

### Policy Record

```json
{
  "scope": "tool",
  "scopeKey": "file_system.write",
  "mode": "approval_required",
  "enabled": true,
  "updatedBy": "admin",
  "updatedAt": "2026-05-07T12:00:00Z"
}
```

### Approval Request

```json
{
  "id": "apr_123",
  "requestId": "req_456",
  "tool": "file_system.write",
  "category": "filesystem",
  "riskLevel": "high",
  "argumentsPreview": "{\"path\":\"/data/wiki/who/alice.md\"}",
  "status": "pending",
  "expiresAt": "2026-05-07T12:05:00Z"
}
```

## API Requirements

| Method | Endpoint | Purpose |
| ------ | -------- | ------- |
| GET | `/api/autonomy/policies` | List effective policy set |
| PUT | `/api/autonomy/policies` | Upsert policy rules |
| GET | `/api/autonomy/approvals` | List approval queue |
| POST | `/api/autonomy/approvals/{id}/approve` | Approve queued action |
| POST | `/api/autonomy/approvals/{id}/deny` | Deny queued action |

## UI Requirements

- Settings page section: autonomy mode, policy table, risk presets.
- Approval inbox: pending items, details panel, approve/deny controls.
- Chat run panel: show tool action status (`suggested`, `waiting_approval`, `executed`, `denied`).

## Security and Privacy

- Only admin role can modify policies and approve/deny actions.
- Approval arguments must mask secrets by default.
- Reviewer actions must be CSRF-protected and authenticated.

## Telemetry

Track:

- `autonomy_decision_total` by mode and outcome.
- `approval_queue_depth` and age percentiles.
- `approval_time_seconds` p50/p95.
- `tool_execution_blocked_total` by reason.

## Rollout Plan

1. Phase 0: decision engine in shadow mode (log-only).
2. Phase 1: enable suggest-only and approval-required modes.
3. Phase 2: enable per-tool rules and queue UI.
4. Phase 3: enforce default policies for high-risk tool categories.

## Acceptance Criteria

- AC-1: 100% of tool executions include a policy decision record.
- AC-2: No high-risk tool executes when configured as `approval_required` without approval.
- AC-3: Approval inbox supports approve/deny/expire with audit logs.
- AC-4: Policy precedence behaves deterministically and is covered by tests.
- AC-5: System fails closed (`approval_required`) on resolver errors.

## Dependencies

- Existing tool metadata categories.
- Auth roles (`admin`) and session integrity.
- Logging pipeline for structured audit events.

## Risks and Mitigations

| Risk | Mitigation |
| ---- | ---------- |
| Reviewer fatigue from too many prompts | Risk presets and bulk policy tuning by category |
| Approval bottlenecks | Expiration + escalation + optional delegated reviewers (future) |
| Incorrect risk classification | Conservative defaults and override per tool |

## Open Questions

- Should approvals support delegated reviewers in v1 or v2?
- What is the default expiration window by risk class?
- Do we require dual-approval for specific destructive tools?

## Implementation Clarifications (v1 Defaults)

Use these defaults unless product or security review overrides them during grooming:

- Policy records live in the existing runtime/admin settings store and are hot-reloaded without a service restart.
- Tool risk classification is explicit metadata. v1 high-risk tools include file writes, terminal execution, outbound sends, and destructive mutations.
- Approval is single-reviewer in v1. Delegation and dual-approval stay out of scope unless a compliance requirement forces them in.
- Approval expiration defaults to 5 minutes for high-risk requests and 15 minutes for medium-risk requests. Expiration resolves to deny.
- Shadow mode records decisions and simulated outcomes but never changes the live execution result.

## Sprint-Ready Engineering Tickets

- [ ] `APE-01` Define the shared policy contracts in `LeanKernel.Core` for scope, mode, risk level, and reason codes, and add precedence fixtures that cover global, route, category, and tool overrides.
- [ ] `APE-02` Implement `AutonomyPolicyResolver` with fail-closed behavior, deterministic precedence resolution, and unit tests for `deny`, `queue_for_approval`, and `execute` outcomes.
- [ ] `APE-03` Wire `ToolExecutionGuard` into the main tool dispatch path so every tool call is evaluated before execution and emits a correlation-linked decision record.
- [ ] `APE-04` Build the approval queue persistence and service layer, including create, approve, deny, expire, masked argument previews, and reviewer audit fields.
- [ ] `APE-05` Add admin APIs and UI flows for policy management and the approval inbox, including risk display, request origin, and deny-comment enforcement.
- [ ] `APE-06` Emit structured telemetry and immutable audit events for resolver decisions, approval actions, execution outcomes, and subsystem failures.
- [ ] `APE-07` Add integration coverage in `LeanKernel.Tests.Integration` for shadow mode, approval-required execution, timeout expiry, and resolver failure fallback before enabling the rollout phases.
