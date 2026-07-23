# Sonar Coverage Activities

## Step-By-Step Activities
1. Confirm baseline Sonar failure metrics and identify highest-impact uncovered files on new code.
2. Add targeted tests around `DocumentUploadEndpoint` (validation, authorization boundary, accepted queue path).
3. Add targeted tests around `AttachmentIngestionMiddleware` (multipart gate, channel authorization, event emission, pass-through behavior).
4. Add targeted tests around `GBrainDocumentStoreClient` if projected or measured `new_coverage` after steps 2-3 remains below 79.5%.
5. Run affected test projects locally and fix failures.
6. Run `scripts/quality/sonarqube-scan.sh` and verify quality gate status is `OK`.
7. Capture evidence and update plan artifacts.

## Test Design Matrix
- `DocumentUploadEndpoint`: empty file, missing channel id, invalid channel id, unreadable channel forbid, tenant scope with empty badge forbid, accepted queue path with staged file.
- `AttachmentIngestionMiddleware`: non-multipart pass-through, multipart without form pass-through, form read failure fallback, empty files pass-through, unreadable channel 403, valid upload emits event and continues.
- `GBrainDocumentStoreClient` (contingency target): not-found handling, malformed payload fallback, filter behavior, and content extraction guards.

## Review Focus
- Tests assert behavior contracts, not implementation details.
- Added tests materially exercise previously uncovered branches.
- No behavior regressions or unauthorized scope changes in production code.
- Sonar gate evidence is reproducible from committed state.
