# Phase 17 Activities

## Step-By-Step Activities (Remaining To Close Phase)

### A. Startup Validation (Only Unmet Gate)

1. Add `Options` validation for `Agents:Telemetry` at startup:
   - `Currency` required and ISO-style uppercase token (`USD` default)
   - cost-estimate maps non-negative when present
   - retain-raw toggle remains compatible with current telemetry write path
2. Wire validation to fail fast on invalid config (`Validate(...).ValidateOnStart()`).
3. Add focused tests for valid/invalid telemetry configuration binding.

### B. Closure Evidence and Sign-Off

4. Update `evidence.md` with current implementation anchors and remove pre-implementation statements.
5. Update `exit-criteria.md` to mark the final gate complete once validation lands.
6. Record final verification evidence and complete the approval table statuses.

### C. Intelligent Brain Delta

7. Extend telemetry schema/capture with evidence-class labels and grounding status (`grounded`, `partial`, `ungrounded`).
8. Add retrieval attribution fields (selected memory keys/classes and ranking scores) for replay analysis.
9. Ensure export schema includes these labels for Phase 23 gating and Phase 04 tuning inputs.

## Review Focus
- Startup validation blocks bad telemetry config before serving traffic.
- Existing telemetry capture/persistence/reporting behavior is unchanged by validation changes.
- Final closure docs match shipped implementation exactly.
