# Phase 2026-08-02 Risk Register

## Risks

| Risk | Impact | Likelihood | Mitigation |
| --- | --- | --- | --- |
| Python extraction is unavailable or fails for Office documents | Inline text injection falls back to notification; library ingestion still succeeds with empty extracted text | Medium | Catch extraction exceptions, log warnings, and preserve the existing ingestion pipeline. |
| Request body rewriting changes the JSON shape unexpectedly | Downstream API clients may reject the rewritten payload | Medium | Rewrite only targeted attachment parts and preserve message ordering, sibling properties, and non-matching content. |
| Large document payloads bloat model context | Model context budget may be exceeded | Medium | Use the configured extraction limit and fall back to the document-search notification once content is truncated or empty. |
| Existing tests regress while adding inline extraction | Feature rollout is blocked by failing unit tests | Low | Keep the change narrowly scoped, cover both success and fallback paths, and run the full unit suite before closing the phase. |
| Unset or empty `ScratchRoot` configuration causes `ArgumentException` in path resolution | Gateway or service runtime errors during extraction | Low | Fallback to `Path.Combine(documentsRoot, "_scratch")` when `ScratchRoot` is empty or null. |
| Payloads containing duplicate data URLs cause dictionary key collision during request rewriting | 500 internal server error during middleware request rewriting | Low | Use indexer assignment (`dict[dataUrl] = replacement`) when building the replacement map. |
