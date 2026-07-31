Given your goals—single canonical storage, cross-scope retrieval, deterministic behavior, predictable UX, and low implementation risk—I would recommend treating **document identity** separately from **authorization provenance**.

The key architectural observation is that **scope is an authorization concern, not an identity concern**. Once a document is visible through one or more permitted scopes, the retrieval layer should ideally reason about the document as a single logical object while retaining enough provenance for auditing and future policy decisions.

## Executive recommendation

| Tool              | Recommendation                                                                                                       |
| ----------------- | -------------------------------------------------------------------------------------------------------------------- |
| `document_search` | **Option 3:** deduplicate by content fingerprint, preserve provenance internally (optionally expose if needed later) |
| `document_list`   | **Option 3** for consistency, although canonical slug dedupe also becomes acceptable once migration completes        |
| Returned payload  | Keep existing output stable; retain provenance internally. Expose only if a future feature requires it.              |

I would not recommend returning duplicate logical documents simply because they were reachable through multiple scopes.

---

# 1. Recommendation for document_search

I would use:

> **Deduplicate by content fingerprint while retaining all matching scope provenance.**

The retrieval pipeline becomes approximately:

```
Query all authorized scopes
        ↓
Collect hits
        ↓
Group by content fingerprint
        ↓
Choose representative hit
        ↓
Return one result
```

Representative selection:

1. Highest score
2. Newest ingestion
3. Lexical slug

Those tie-breakers are deterministic and sensible.

The representative contributes:

* score
* excerpt
* title
* metadata

while the merged object internally records

```
matchedScopes = [...]
matchedSlugs = [...]
```

even if those are not returned.

### Why fingerprint instead of slug?

Your architecture is explicitly moving toward a **canonical user document**.

The fingerprint represents:

> "This is the same document."

The slug represents:

> "This is where I found it."

Those are different concepts.

If the retrieval layer queries multiple scopes simultaneously, slug identity is no longer the logical identity.

---

# 2. Recommendation for document_list

I would use exactly the same grouping rule.

```
Group by fingerprint
```

Ordering:

1. newest ingestion
2. lexical slug

Consistency between search and list has real UX value.

A user should never see:

```
Search:
Budget.xlsx

List:
Budget.xlsx
Budget.xlsx
Budget.xlsx
```

That inconsistency creates confusion.

---

# 3. Pros, cons, and failure modes

## Option 1 — dedupe by canonical slug

### Pros

Very simple.

Cheap.

No ambiguity.

Easy to reason about.

### Cons

Works only if every logical document truly has one canonical slug.

During migration or replication you'll still see duplicates.

If multiple canonical slugs accidentally exist for identical content, duplicates appear.

### Failure modes

Migration bugs create duplicate documents.

Reimports create multiple slugs for identical content.

Historical data remains noisy.

---

## Option 2 — dedupe by fingerprint

### Pros

Treats document identity correctly.

Survives migration.

Survives multiple storage locations.

Produces the cleanest UX.

Works naturally across scopes.

### Cons

Assumes fingerprint quality.

Different metadata attached to identical content becomes collapsed.

You lose visibility into where the document was found unless separately retained.

### Failure modes

Fingerprint collision.

This is usually negligible with modern hashes.

A more realistic issue:

Two intentional copies of identical content that users conceptually consider different.

Example:

```
HR Handbook.pdf

Sales Handbook.pdf
```

If byte-identical, fingerprint says they're the same.

Whether that's acceptable depends on product semantics.

---

## Option 3 — fingerprint + provenance

This is my preferred design.

### Pros

Best UX.

No duplicate documents.

No loss of auditability.

Future-proof.

Supports future features like

```
Available through:

Project A
Finance
Private
```

without changing retrieval architecture.

Supports diagnostics.

Supports authorization debugging.

Supports ranking improvements.

### Cons

Slightly more implementation complexity.

Need merge logic.

Need provenance model.

Worth it.

### Failure modes

Mostly the same as Option 2.

Much lower operational risk because provenance remains available.

---

## Option 4 — no dedupe

### Pros

Extremely simple.

No hidden information.

Reflects storage exactly.

### Cons

Poor UX.

Search quality degrades.

Ranking becomes noisy.

LLMs receive redundant context.

Users see repeated results.

Future canonicalization gains little benefit.

### Failure modes

One document appears five times.

Top search results become:

```
Quarterly Report
Quarterly Report
Quarterly Report
Quarterly Report
Quarterly Report
```

This actively hurts retrieval quality.

---

# Ordering

Your proposed ordering is good.

For search:

```
highest score
→ newest ingestion
→ lexical slug
```

I would keep it.

For list:

```
newest ingestion
→ lexical slug
```

Also reasonable.

I would avoid using scope priority as an ordering signal unless product semantics explicitly require it.

---

# Provenance

I would distinguish three audiences.

## Internal retrieval

Keep complete provenance.

Example:

```
fingerprint

matchedScopes

matchedSlugs

matchedPermissions

matchedChannels

representativeSlug

representativeScope
```

This is extremely valuable for:

* debugging
* authorization
* telemetry
* auditing
* future ranking

---

## Tool output

I would keep it unchanged today.

Backward compatibility has value.

Unless consumers have asked for provenance, don't expose it.

---

## Optional future enhancement

If needed later:

```
{
  document,
  score,
  excerpt,
  provenanceCount: 3
}
```

or

```
availableScopes:
[
    ...
]
```

This avoids breaking existing clients while allowing richer experiences.

---

# Better alternatives

I think there is one refinement worth considering.

## Separate logical identity from storage identity

Instead of deduplicating directly on fingerprint, introduce an explicit logical document identifier.

For example:

```
DocumentId

Fingerprint

StorageLocations[]
```

where

```
Document
    Id
    Fingerprint
    Metadata

StorageLocation
    Slug
    Scope
    Channel
    Permissions
    IngestionTime
```

Retrieval becomes:

```
Authorized storage locations
        ↓
Map to DocumentId
        ↓
Merge
        ↓
Rank
```

Advantages:

* Fingerprints can change if hashing evolves.
* Multiple fingerprints (e.g., normalization changes) can map to one logical document.
* Metadata can evolve independently of storage.
* Supports future scenarios such as aliases, superseded versions, or migrated content without changing retrieval semantics.

This is a larger architectural investment, so if low implementation risk is the priority, fingerprint-based grouping is a pragmatic and robust choice.

## Overall recommendation

For a system converging on canonical user-scoped storage while still querying across authorized scopes, the most balanced design is:

* **Use content fingerprint as the logical deduplication key** for both `document_search` and `document_list`.
* **Merge results deterministically**, selecting the representative by highest score, then newest ingestion time, then lexical slug for search, and by newest ingestion time then lexical slug for list.
* **Retain full scope provenance internally** for auditing, diagnostics, and future enhancements, while **keeping current tool outputs unchanged** unless provenance becomes a product requirement.

This cleanly separates document identity from authorization, yields deterministic behavior, minimizes duplicate results, preserves backward compatibility, and leaves room for future evolution with relatively low implementation risk.
