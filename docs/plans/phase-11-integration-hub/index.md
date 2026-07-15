# Phase 11 Integration Connector Hub

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Introduce an OAuth-based connector framework so the assistant can securely act on a user's third-party accounts (Google Workspace, Microsoft 365, GitHub, Notion, and similar). This is the foundation that turns generic HTTP/skill tools into real, per-user authorized integrations, and it is the prerequisite for the email and calendar assistant (Phase 12) and most action-taking features.

## Objective Notes
Connectors are person-scoped (Phase 10) so a linked user's authorizations are available on every channel, and every connector-backed action is subject to the autonomy/approval engine (Phase 14) and tool governance.

## Scope
This phase delivers the connector abstraction, per-user credential storage, OAuth authorization flows, token lifecycle management, and the exposure of connector operations as governed tools. It does not implement specific high-level assistant features (email triage, calendar scheduling) beyond a minimal reference connector to prove the framework.

## In Scope
- A connector abstraction: a provider descriptor (scopes, endpoints, auth type) and a runtime contract for authorized calls.
- OAuth 2.0 / OIDC authorization-code + refresh flows, plus API-key/PAT connectors where OAuth is unavailable.
- Secure per-person credential vault: encrypted-at-rest token storage keyed by person + provider, with rotation and revocation.
- Token lifecycle: refresh, expiry handling, scope tracking, and re-consent prompts.
- Connector registry that exposes provider operations to the runtime as governed MAF tools, subject to tool governance and egress rules.
- A connect/disconnect management flow (initiate authorization, store grant, list connected accounts, revoke).
- At least one reference connector (e.g., a minimal Google or Microsoft Graph read scope) to validate the framework end to end.
- Configuration for provider client credentials/secrets (sourced from `/run/secrets` or env), redirect URIs, and enabled providers; startup validation.
- Tests for auth flow, token refresh/expiry, encryption, revocation, per-person isolation, and governed tool exposure.

## Out of Scope
- Full email/calendar feature logic (Phase 12).
- The autonomy/approval engine itself (Phase 14) — this phase integrates with it once available and gates writes conservatively until then.
- Building provider UIs beyond a minimal connect/disconnect surface.

## Entry Criteria
- Tool runtime + governance (Phase 01) and egress validation are operational.
- Person-scoped identity (Phase 10) is available so credentials follow the person across channels.
- Secret resolution pattern exists (`DynamicSkillTool` secret handling, `/run/secrets`).

## Exit Criteria
A user can authorize a third-party provider once, have those credentials available across their channels, and the assistant can perform governed, per-user authorized calls with correct token lifecycle and revocation. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
