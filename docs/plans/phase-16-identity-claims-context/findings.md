# Phase 16 Deep Review Findings

Contextual and architectural review performed on 2026-07-15 against the current worktree
for identity linking/unlinking behavior in:

- `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs`
- `src/Common/LeanKernel.Core/Interfaces/IIdentityResolver.cs`
- `test/LeanKernel.Tests.Unit/Identity/IdentityResolverTests.cs`

This review follows `.agents/prompts/deep-review.prompt.md` and intentionally excludes
pure static-analysis concerns (style, generic smells, CVEs, and syntax checks).

Severity scale used here:

- **Critical**: high-probability tenant/data integrity break with production impact.
- **Major**: correctness issue with clear user/data impact but narrower blast radius.
- **Suggestion**: hardening and maintainability improvements.

---

## Critical

### C1 — `LinkUsersAsync` can rewrite person clusters outside the requested tenant
- **File/Module:** `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:339-341`
- **The Issue:** `LinkUsersAsync` verifies source and target are members of the provided tenant, but when merging clusters it updates **all** users with `PersonId == targetPersonId` with no tenant filter:
  ```csharp
  var cluster = await context.Users
      .Where(user => !user.IsDeleted && user.PersonId == targetPersonId)
      .ToListAsync(ct);
  ```
  If that canonical person id is shared by users in other tenants, their linkage is silently rewritten.
- **Why Static Analysis Missed It:** The query is type-safe and valid SQL translation. The defect is semantic and cross-entity: the method-level tenant guard is not propagated to the merge set.
- **Impact:** Cross-tenant identity partitioning corruption; unrelated tenant users can be re-mapped to a different canonical person during a seemingly tenant-scoped operation.
- **Recommended Fix:** Constrain the merge set to users that belong to the same tenant before mutating `PersonId`.
  ```csharp
  var clusterCandidates = await context.Users
      .Where(user => !user.IsDeleted && user.PersonId == targetPersonId)
      .ToListAsync(ct);

  foreach (var member in clusterCandidates)
  {
      if (await UserBelongsToTenantAsync(context, member.Id, tenantId, ct))
      {
          member.PersonId = mergedPersonId;
      }
  }
  ```
  Preferably, materialize tenant membership once and do a single set-based filtered update to avoid N+1 checks in high-cardinality clusters.

---

## Major

### M1 — `UnlinkUserAsync` does not isolate an anchor user from its cluster
- **File/Module:** `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:373`
- **The Issue:** `UnlinkUserAsync` always does `user.PersonId = user.Id;`. For anchor users (where `user.PersonId == user.Id` and other rows point to that person id), this is a no-op and the user remains linked through shared canonical id.
- **Why Static Analysis Missed It:** The assignment is syntactically valid and appears to satisfy the API on a superficial read. The bug only appears for the anchor edge case requiring relational state reasoning.
- **Impact:** The API contract (`IIdentityResolver.UnlinkUserAsync`) says it re-isolates the user, but for one class of users it silently fails to do so.
- **Recommended Fix:** Add an explicit anchor-path branch. If unlinking an anchor user with dependents, move other members to a replacement canonical person id and then isolate the requested user.
  ```csharp
  var members = await context.Users
      .Where(u => !u.IsDeleted && u.PersonId == user.PersonId && u.Id != user.Id)
      .ToListAsync(ct);

  if (members.Count > 0 && user.PersonId == user.Id)
  {
      var replacementPersonId = members[0].Id;
      foreach (var member in members)
      {
          member.PersonId = replacementPersonId;
      }
  }

  user.PersonId = user.Id;
  ```
  The reassignment set should still be tenant-bounded.

---

## Suggestions (test and resilience hardening)

### S1 — Missing regression test for anchor-user unlink behavior
- **File/Module:** `test/LeanKernel.Tests.Unit/Identity/IdentityResolverTests.cs:263`
- **Observation:** Existing test unlinks `userB` (non-anchor) only; no coverage for unlinking `userA` (anchor).
- **Recommendation:** Add a test that links A/B, unlinks A, then asserts:
  - A resolves to `PersonId == A.Id`
  - B resolves to a canonical id that is **not** `A.Id`
  - Cluster integrity remains stable across repeated unlink calls (idempotency check).

### S2 — Missing tenant-scope regression for cluster merge updates
- **File/Module:** `test/LeanKernel.Tests.Unit/Identity/IdentityResolverTests.cs`
- **Observation:** There is no test proving `LinkUsersAsync` does not mutate users outside the specified tenant once cluster merge logic executes.
- **Recommendation:** Add a two-tenant setup where a user in tenant B intentionally shares `target.PersonId`; call `LinkUsersAsync(tenantA, ...)` and assert tenant B rows remain unchanged.

---

## Final Verdict

**No-Go** until C1 and M1 are addressed and guarded by regression tests. The current implementation risks cross-tenant identity corruption and incomplete unlink semantics under realistic cluster states.
