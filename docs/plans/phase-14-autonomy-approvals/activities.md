# Phase 14 Activities

## Step-By-Step Activities
1. Define an action risk model: classify actions by side-effect severity (read / reversible write / irreversible / financial) and map each to a required autonomy level.
2. Implement a policy engine that evaluates per-person and per-tenant policy plus the action classification to decide auto-approve, require-approval, or block; ship conservative safe defaults.
3. Establish a single enforcement interception point in the tool/action execution path so no governed action can bypass policy evaluation.
4. Implement the approval workflow: create a request, deliver it to the user's preferred/available channel (Phase 06), capture confirm/deny, and expire unanswered requests with a default-deny outcome and quiet-hours handling.
5. Implement a durable, tamper-evident action audit log (who/what/when/decision/inputs-summary/outcome), person- and tenant-scoped, backed by diagnostics persistence (Phase 08) and queryable via its API.
6. Provide integration hooks/adapters so Phases 11–13 route their writes through the engine, and define fail-safe behavior when the engine is disabled (block or draft-only).
7. Add configuration (default autonomy levels, per-action overrides, approval timeout, quiet-hours behavior) and startup validation.
8. Add tests: classification correctness, policy decisions, approval lifecycle + expiry-default-deny, enforcement no-bypass, audit completeness/immutability, and person/tenant isolation.
9. Document the autonomy model, approval flow, and audit log in `docs/features/` and `docs/operations/`.

## Review Focus
- No governed side-effecting action can bypass the enforcement point.
- Unanswered/expired approvals default to deny, never allow.
- Audit log is complete and tamper-evident; no action executes unlogged.
- Policy defaults are safe (least autonomy) and require explicit opt-in to raise.
- Per-person/tenant policy isolation holds.
- Approval prompts cannot be spoofed or replayed to grant unintended actions.
