# Phase 22 Activities

## Step-By-Step Activities

### Core Claim Model (independent of Phase 07)
1. Define canonical claim/evidence entities and scope-safe repository contracts (see interface specs below).
2. Implement contradiction detection and temporal precedence rules.
3. Implement deterministic resolution policies and supersession workflows.
4. Add confidence decay/refresh logic and reconciliation jobs.

### Dream Integration (requires Phase 07 Dream orchestration)
5. Integrate Dream outputs into the truth lifecycle pipeline — hook `DreamCycleJob` phase-completion reports to emit canonical claims from Dream-derived facts. **Blocked until Phase 07 Dream orchestration is operational.**

### Cross-cutting
6. Add diagnostics and telemetry hooks for conflict and confidence transitions.
7. Add unit/integration tests and update documentation.

### Interface Specifications

#### Entity: `CanonicalClaim`
```
- Id: Guid (PK)
- TenantId: Guid
- UserId: Guid
- ChannelId: Guid
- Subject: string (normalized fact subject)
- Predicate: string (relationship or attribute)
- Object: string (fact value)
- Confidence: decimal (0.0–1.0)
- ValidFrom: DateTimeOffset
- ValidTo: DateTimeOffset? (null = currently valid)
- SupersededBy: Guid? (FK to replacement claim)
- SourceType: enum { Ingestion, Dream, Manual }
- SourceId: string (fingerprint, Dream run id, or user id)
- Provenance: string (original content reference)
- ResolutionPolicy: enum { AutoResolve, FlagForReview, RetainWithUncertainty }
- CreatedAt: DateTimeOffset
- UpdatedAt: DateTimeOffset
```

#### Entity: `ClaimEvidence`
```
- Id: Guid (PK)
- ClaimId: Guid (FK to CanonicalClaim)
- EvidenceType: enum { SourceDocument, DreamOutput, UserAssertion, ExternalFeed }
- Reference: string (path, run id, or URL)
- Weight: decimal (0.0–1.0)
- CapturedAt: DateTimeOffset
```

#### Repository Contracts (in `LeanKernel.Logic`)
```csharp
public interface ICanonicalClaimRepository
{
    Task<CanonicalClaim?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CanonicalClaim>> GetBySubjectAsync(string subject, Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<CanonicalClaim>> GetConflictSetAsync(string subject, Guid tenantId, CancellationToken ct);
    Task<CanonicalClaim> UpsertAsync(CanonicalClaim claim, CancellationToken ct);
    Task SupersedeAsync(Guid claimId, Guid replacementId, CancellationToken ct);
}

public interface IClaimEvidenceRepository
{
    Task AddEvidenceAsync(ClaimEvidence evidence, CancellationToken ct);
    Task<IReadOnlyList<ClaimEvidence>> GetByClaimIdAsync(Guid claimId, CancellationToken ct);
}

public interface IContradictionDetector
{
    Task<IReadOnlyList<ConflictSet>> DetectConflictsAsync(string subject, Guid tenantId, CancellationToken ct);
}

public interface IConfidenceDecayService
{
    Task ApplyDecayAsync(CancellationToken ct);
    Task RefreshAsync(Guid claimId, decimal boost, CancellationToken ct);
}
```

#### Test Targets
- Claim upsert, supersession chain traversal, and temporal precedence ordering
- Contradiction detection across same-scope claims with overlapping validity windows
- Resolution policy execution: auto-resolve picks highest-confidence non-expired claim; flag-for-review emits diagnostic; retain-with-uncertainty preserves both
- Confidence decay reduces over time; refresh boosts and resets decay clock
- Scope isolation: claims from tenant A are invisible to tenant B queries
- Dream integration hook: Dream phase-completion event produces canonical claims with correct `SourceType = Dream`

## Review Focus
- Contradiction detection correctness and deterministic conflict resolution.
- Scope isolation and no cross-tenant/cross-policy leakage.
- Backward compatibility for existing memory/document retrieval behavior.
