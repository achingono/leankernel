# Phase 05 Tool Expansion

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Extend the rebuild's existing tool runtime (Phase 01) with the remaining source-repo tool categories so the `leankernel` agent is useful by default: a full filesystem tool suite, data tools (database query, JSON transform, CSV/XLSX read/write), HTTP/web-fetch tools, a sandboxed browser automation tool, and a document ingestion pipeline. All tools remain provider-agnostic MAF `AIFunction`s registered through the existing `IToolRegistry` and constrained by the existing tool governance and egress validation.

## Scope
This phase adds tool capabilities only; it reuses the Phase 01 registry, governance policy, filesystem boundary support, and dynamic-skill loading. It does not change the turn pipeline, model routing, or UI. Browser automation depends on an external service and is delivered behind a capability probe so the runtime degrades cleanly when the service is absent.

## In Scope
- Filesystem tool suite beyond the current `file_search`: read, write, edit, list, copy, move, delete, stat, touch, chmod, and text extraction — all bounded by the configured filesystem root and governance.
- Data tools: parameterized database query (read-only by default), JSON transform, and CSV/XLSX read/write.
- Internet tools: `web_fetch` and a direct `http_request` tool, subject to the existing egress validation (no private/loopback unless allowed).
- Browser tool: a browser-automation surface backed by an external automation service (Webwright/Playwright), gated behind a health/capability probe like the GBrain wiki tools.
- ~~Document ingestion: an ingestion queue, folder-monitor hosted service, backfill service, and document-library service that feed content into the knowledge/memory stores.~~ **Moved**: document ingestion with full channel-awareness and memory policy enforcement is now [Phase 21](../phase-21-channel-document-ingestion/index.md).
- Configuration for filesystem boundaries, database connections, browser service endpoint, and ingestion folders under existing `Files`/`Agents`/`ConnectionStrings` sections.
- Governance defaults, allowlisting, and per-tool tests including boundary and egress enforcement.

## Out of Scope
- The Phase 01 built-in tools already implemented (`web_search`, `file_search`, `calculate`, aggregation, `wiki_*`, dynamic `SKILL.md`).
- Turn pipeline, routing, learning, scheduler, diagnostics, and UI.
- Building the external browser automation service itself (only the client + governance surface).

## Entry Criteria
- Phase 01 tool runtime (`IToolRegistry`, `ToolGovernancePolicy`, `FileSystemSupport`, `ToolRuntimeStartup`) is operational.
- Egress validation (`EgressValidator`) and capability-probe pattern (`GBrainCapabilityCheck`) are available for reuse.
- Source references captured as behavioral targets: `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/FileSystem/*`, `BuiltIn/Data/DatabaseQueryTool.cs`, `JsonTransformTool.cs`, `CsvXlsxReadWriteTool.cs`, `BuiltIn/Internet/WebFetchTool.cs`, `HttpRequestTool.cs`, `BuiltIn/Browser/BrowserToolDefinitions.cs`, `WebwrightClient.cs`, `WebwrightHealthProbe.cs`, and `DocumentIngestionHostedService.cs`, `DocumentFolderIngestionHostedService.cs`, `DocumentBackfillService.cs`, `DocumentLibraryService.cs`, `DocumentIngestionQueue.cs`.

## Exit Criteria
The agent can execute the expanded filesystem, data, internet, and browser tools under governance and boundary enforcement, and documents placed in monitored folders are ingested into the knowledge store. See `exit-criteria.md`.

## Roles
- Owner: Rebuild maintainer
- Reviewer: Separate agent session / model review
- Approver: Repository owner
