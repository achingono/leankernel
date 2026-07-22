# Phase 20 Addendum

## Implementation Decisions I Would Consider Doing Differently

1. I would avoid making `IPolicy<TEntity>` the first-class enforcement mechanism for entity access.

   The repository already has a fail-closed authorization and partitioning path through
   `IPermit<TEntity>`, `IFilter<TEntity>`, and `IRepository<TEntity>`. If Phase 20 needs
   richer policy decisions, I would keep repository enforcement where it is and add
   narrower domain policies on top of it for decisions such as identity linking,
   cross-channel memory visibility, and budget actions. That keeps one hard gate for
   data access and avoids rebuilding a second, easier-to-bypass authorization surface.

2. I would stage the event spine as a new event envelope plus projection boundary rather
   than trying to reinterpret existing turn rows as an event log.

   The current persistence model intentionally deduplicates retries, allows soft-delete,
   stores compaction markers, and represents tool activity as chat turns. That is useful,
   but it is not the same thing as a lossless append-only event stream. I would introduce
   an explicit event producer contract first, keep `SessionEntity`, `TurnEntity`, and
   `TurnTelemetryEntity` as projections/read models during the transition, and only later
   decide whether older data should be backfilled into the new spine.

## Why These Alternatives Matter

- They preserve the working Phase 19 enforcement model instead of replacing it midstream.
- They let Phase 20 prove value with one migrated consumer path before a broader rewrite.
- They reduce the risk of identity-scope regressions and lossy event backfills.
