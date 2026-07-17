# Phase 17 Activities

## Step-By-Step Activities
1. Add a docker e2e fixture that reads runtime endpoints and credentials from environment variables.
2. Add DB helpers to create a test user and channel sender binding for authenticated channel-claim flow.
3. Add GBrain MCP helpers to seed scoped memory pages and search scoped namespace content.
4. Add a lifecycle e2e test that:
   - creates a test user in database,
   - creates test memory data,
   - submits request to `/v1/responses`,
   - verifies memory retrieval in model output,
   - verifies memory persistence by searching for post-turn facts.
5. Add docs for running these tests against a live docker deployment.
6. Run targeted test project build/test verification.

## Review Focus
- Correct identity partitioning (`tenant`, `person`, `channel`) in seeded memory slug.
- Safety of DB setup/cleanup and deterministic test isolation via unique ids.
- Avoiding false positives in retrieval/persistence assertions.
- Clear env-var-based gating so tests only run intentionally.
