# PRD: Fix Build gbrain Workflow Failure by Sequencing Release Creation

## Context
- Failing run: `Build and Publish Images` (`run_id=27319577798`, `job_id=80707537714`, job `Build gbrain`).
- The failing step is `Create GitHub Release (with image tags)` in matrix job `build-images`.
- Log evidence: `403 Resource not accessible by integration` from `softprops/action-gh-release@v2` while creating release for `v0.0.1`.
- Current workflow executes release creation once per matrix entry (`engine`, `gbrain`, `webwright`), so failure in one matrix leg cancels siblings.

## Goal
- Ensure image builds complete first for all matrix images.
- Execute release creation once, after successful completion of all image builds.
- Eliminate per-matrix release race/failure behavior and correct token permissions for release creation.

## Non-Goals
- Changing Docker image build logic, tags, or platform targets.
- Refactoring unrelated CI workflows.

## Plan Review (Different Model)
- Independent review requested for the plan; operationally constrained in this environment, so the plan was self-reviewed against failure logs and workflow semantics.

Review conclusions incorporated:
- Keep release creation out of matrix jobs.
- Add dedicated release job with `needs: build-images`.
- Grant `contents: write` where release API is called.
- Preserve tag-only behavior.

## Reviewed Implementation Plan
1. Remove release-note and release-creation steps from matrix `build-images` job.
2. Keep `build-images` permissions scoped to image publishing (`contents: read`, `packages: write`).
3. Add new `create-release` job with `needs: build-images` and `if: github.ref_type == 'tag'`.
4. In `create-release`, compute previous tag and release notes once.
5. Generate deterministic image tag list for all matrix images from `${GITHUB_REF_NAME}` and `${GITHUB_SHA}`.
6. Call `softprops/action-gh-release@v2` once using `contents: write` permission.
7. Validate workflow syntax and run required build/test/quality commands per repository process.

## Acceptance Criteria
- `Build and Publish Images` run no longer fails at `Build gbrain` due to release creation.
- Release creation occurs once after all image builds pass.
- Generated release body includes notes and image tags for `engine`, `gbrain`, and `webwright`.

## Rollback
- Revert `.github/workflows/publish.yml` to prior revision.
- Re-run tag workflow to confirm prior behavior is restored (if needed for diagnostic comparison).
