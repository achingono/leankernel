**Recommended strategy**

1. **document_search**: Option 3 — deduplicate by content fingerprint, preserve scope provenance in the returned payload (or at least retain it internally and surface a clean single hit).
2. **document_list**: Option 3 as well (or a lighter variant of it). Prefer fingerprint-based uniqueness with optional provenance; fall back to deterministic ordering only when fingerprints collide or are unavailable.

### Why this choice
- Canonical slug alone (option 1) is fragile once you move to a single user-scoped storage path. The same logical document can appear under multiple historical or channel-derived paths during migration, or the empty-guid placeholder / tenant/user structure can produce slug variants that are not truly distinct.
- Pure fingerprint (option 2) correctly collapses true duplicates but discards useful context about *which* permitted scopes contributed the hit. Callers that still care about channel authorization or audit trails lose information.
- No deduplication (option 4) produces noisy, non-deterministic-feeling results across channels even when content is identical; that violates the “one logical document” goal and makes ranking harder to reason about.
- Fingerprint + provenance gives you the clean UX of a single result while keeping the authorization and lineage story intact. Deterministic secondary keys (score → ingestion time → lexical slug for search; ingestion time → lexical slug for list) make the survivor choice predictable.

### Pros / cons / failure modes of each option

| Strategy | Pros | Cons / failure modes |
|----------|------|----------------------|
| **1. Canonical slug** | Simple; works if every document truly has one stable path. Low code change. | Breaks during the channel→user migration (old channel-scoped paths vs new user-scoped paths). Same content under different historical slugs appears multiple times. Empty-guid placeholder or tenant variations create false uniqueness. |
| **2. Content fingerprint** | Correctly collapses identical content regardless of path. Matches the storage model’s intent (fingerprint in the path). Deterministic once the winner is chosen. | Loses all scope/channel provenance. If two scopes legitimately hold near-identical but intentionally different documents (e.g., channel-specific annotations or versions), you hide a meaningful distinction. Fingerprint collisions (rare but possible with weak hashes) or missing fingerprints cause silent data loss. |
| **3. Fingerprint + provenance** | Best of both: one logical hit + retained context. Supports audit, debugging, and future “show me all scopes this came from.” Backward-compatible if provenance is additive. | Slightly more payload and merge logic. Callers must be taught (or ignore) the new field. If provenance is poorly designed it can re-introduce noise. |
| **4. No dedupe, only ordering** | Zero risk of hiding distinctions; simplest implementation. Fully deterministic ranking. | Noisy results; same document appears once per permitted scope. Ranking becomes less useful (score ties are common). UX suffers when users see near-identical rows. |

### Provenance recommendation
Return a compact provenance structure to the caller (preferred) or keep it strictly internal and expose only a stable canonical identifier + the chosen winner’s metadata.

- Prefer returning it: a small array or map of `{scope, originalSlug, score?, ingestedAt?}` (or even just the contributing scope IDs) lets higher layers decide how to present “this document is available in channels X and Y.” It also aids debugging authorization surprises.
- Keep it internal-only if tool contracts are frozen and you cannot extend the payload without breaking clients; in that case still compute the merge with provenance so future versions can surface it.
- Never ignore it entirely—authorization still depends on the current channel’s permitted scope set, so the retrieval layer needs the information even if the final tool response hides it.

Make provenance additive and optional so existing compact result shapes remain backward-compatible.

### Better alternatives / refinements (not listed)
- **Fingerprint primary + canonical slug as tie-breaker / display key**. Use the content fingerprint for uniqueness; surface the user-scoped canonical slug as the stable ID the rest of the system should prefer going forward. This eases the migration.
- **Soft dedupe with “related” links**. Collapse exact fingerprint matches into one primary result, but attach a small “also present in scopes \ldots” or “variants” list for near-duplicates (e.g., different ingestion times or minor content diffs). Useful if you later add versioning.
- **Two-phase merge**. First union by fingerprint across the permitted scopes, then apply the deterministic ordering rules only to the survivors. This keeps the ranking logic simple and independent of how many scopes contributed.
- **Scope-priority override** (optional, low risk). Allow a small, explicit priority order among scopes (e.g., “current channel first”) before falling back to score / time / slug. Only introduce this if product feedback shows that pure fingerprint+score is insufficient.
- **Explicit “canonical document ID”** that is independent of both slug and fingerprint (e.g., a content-addressable or UUID stored with the document). Longer-term this is the cleanest, but it requires a storage change and is higher implementation risk than the fingerprint approach you already have in the path.

### Practical ordering rules (keep them)
- Search: highest score → newest ingestion time → lexical slug (of the chosen canonical path).
- List: newest ingestion time → lexical slug.

These are stable, easy to test, and do not depend on the number of scopes.

### Implementation notes for low risk
- Compute the fingerprint once at ingestion (you already embed it in the path) and treat it as authoritative.
- During the transition period, treat both old channel-scoped and new user-scoped paths as candidates; the fingerprint merge will collapse them.
- Keep the tool response shape additive: existing fields stay the same, new optional `provenance` (or `scopes`) field appears only when multiple contributors existed.
- Unit-test the merge with: identical fingerprints across scopes, near-identical content, missing fingerprints, score ties, and the empty-guid placeholder cases.
- Log the pre-merge candidate set (behind a debug flag) so you can diagnose any unexpected collapsing.

This gives you one logical document, predictable ranking, retained authorization context, and a migration-friendly path with minimal surprise for callers.