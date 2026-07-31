Moving to a canonical, user-scoped storage model is a strong architectural decision. It reduces storage bloat and simplifies lifecycle management, but as you've noted, it shifts the complexity to the retrieval and authorization layers.

Here is the architectural assessment and recommendation for your deduplication strategy.

### 1. Recommendation for `document_search`

**Recommend: Option 3 (Deduplicate by content fingerprint but preserve scope provenance).**

In a search context (especially if these tools are feeding an LLM agent or a compact UI), relevance and token/space efficiency are paramount.

* **Why:** Flooding the search results with the exact same document snippet three times just because it exists in three authorized channels destroys the signal-to-noise ratio.
* **How to apply the ordering:** Group the results by fingerprint. For the merged result, take the highest score among the duplicates for ordering, use the newest ingestion time as the tie-breaker, and retain the canonical slug.

### 2. Recommendation for `document_list`

**Recommend: Option 3 (Deduplicate by content fingerprint but preserve scope provenance).**

* **Why:** A catalog list should represent logical files. If a user asks "List my documents," seeing the same file listed multiple times looks like a system bug.
* **How to apply the ordering:** Group by fingerprint. Order the deduplicated list by the newest ingestion time across all merged instances, falling back to the lexical slug.

---

### 3. Pros, Cons, and Failure Modes

| Strategy | Pros | Cons | Failure Modes |
| --- | --- | --- | --- |
| **1. Dedupe by canonical slug** | Simplest to implement. $O(1)$ dictionary grouping. 1:1 mapping with the new storage path. | Loses legacy duplicates (if old channel-scoped slugs haven't been fully migrated, they won't dedupe against the new canonical ones). | Fails to dedupe legacy items against new items, resulting in duplicates escaping to the client. |
| **2. Dedupe by fingerprint** | True logical deduplication regardless of storage path (handles both legacy and new canonical slugs perfectly). | Loses the context of *where* the document was found, which might be critical for authorization auditing or UX. | If two distinct documents coincidentally hash to the same fingerprint (extremely rare but possible depending on the hash), one is silently hidden. |
| **3. Dedupe by fingerprint + preserve provenance** | **(Winner)** Best UX. Handles legacy/new paths. Retains contextual metadata for the caller. | Highest implementation complexity. Requires merging logic (e.g., aggregating scopes into an array) at query time. | Overly large provenance arrays could bloat the payload if a document is shared across thousands of channels. |
| **4. No dedupe (Ordering only)** | Trivial to implement. Zero risk of hiding meaningful data. | Terrible UX. Floods tool outputs, wastes LLM context windows, and frustrates end users. | "Duplicate blindness"—users or agents miss the 4th distinct document because the top 3 slots are the same file from different channels. |

---

### 4. Handling Provenance

**Provenance must be returned to the caller.**

Do not ignore it, and do not retain it strictly internally.

* **For LLM Agents:** If the caller is an AI agent, knowing that a document was sourced from the "HR Policies" channel vs. the "Drafts" channel provides vital context for how much weight to give the document when answering a user's prompt.
* **For UIs:** Frontends often need to render "Found in: Channel A, Channel B" so users understand why they have access to it.
* **Backward Compatibility:** You can achieve this without breaking the existing tool outputs by appending a new, optional array property (e.g., `matched_scopes: string[]` or `provenance_channels: string[]`) to the returned document object. Older clients will ignore it; updated clients can utilize it.

---

### 5. Better Alternatives

While query-time merging (Option 3) is a solid choice, there are two architectural alternatives worth considering to reduce compute overhead at the retrieval layer:

**Alternative A: Index-Time Deduplication (The "Array of Scopes" approach)**
Instead of storing multiple index entries and merging them at query time, update your search index (e.g., Elasticsearch, Azure AI Search, or GBrain's equivalent) to store exactly **one** canonical document record.

* Add a field like `authorized_scopes: ["channel-A", "channel-B"]`.
* At query time, the search filter simply applies `authorized_scopes INTERSECTS user_current_scopes`.
* *Benefit:* Deduplication is handled natively by the index. The query layer does zero merging, pagination works perfectly out-of-the-box, and scoring is calculated once.

**Alternative B: Late Materialization (Two-Pass Query)**
If query-time merging messes up your pagination (e.g., you request 10 items, but after deduplication you only have 6), use a two-pass approach.

* **Pass 1:** Query the index for just the `document_id` and `score`. Deduplicate these lightweight IDs in memory.
* **Pass 2:** Fetch the rich payloads (short excerpts, catalog entries) only for the deduplicated IDs that fall into the current page slice.