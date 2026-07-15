# Phase 05 Activities

## Step-By-Step Activities
1. Inventory current tools and governance categories; define new tool categories (filesystem, data, internet, browser, ingestion) and their default allowlist posture.
2. Implement the filesystem tool suite (read/write/edit/list/copy/move/delete/stat/touch/chmod/extract) on top of `FileSystemSupport`, enforcing the configured root boundary and rejecting path traversal.
3. Implement data tools: read-only-by-default database query with parameterization, JSON transform, and CSV/XLSX read/write; add the required package dependencies.
4. Implement internet tools: `web_fetch` and `http_request` routed through `EgressValidator`, honoring private/loopback rules and secret resolution.
5. Implement the browser tool client against the external automation service, gated by a health/capability probe; degrade the tool cleanly when the service is unavailable.
6. Implement document ingestion: an ingestion queue, a folder-monitor hosted service, a backfill service, and a document-library service that push content into the knowledge/memory stores with fingerprint-based dedupe.
7. Register all tools through the existing `IToolRegistry` and apply governance; add configuration keys and startup validation.
8. Add per-tool tests: filesystem boundary/traversal, data-tool correctness and read-only enforcement, egress enforcement, browser probe degradation, and ingestion dedupe/idempotency.
9. Update tool governance and configuration docs and the tools feature page.

## Review Focus
- Filesystem tools cannot escape the configured root (traversal, symlink, absolute-path abuse).
- Database query is read-only by default and parameterized (no injection).
- Internet tools honor egress rules and never leak secrets.
- Browser tool degrades cleanly when the service is down (no hard failures).
- Ingestion is idempotent and respects tenant/user/channel scoping.
- All tools are governed and disabled-by-default where risky.
