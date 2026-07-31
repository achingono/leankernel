# Phase SonarQube Quality Gate Fix - Activities

## Step-By-Step Activities

1. Create `JwtSecurityTokenGeneratorTests.cs` with tests covering:
   - Token generation with dev secret key (no SecretKey configured)
   - Token generation with explicit SecretKey
   - Token generation with custom Issuer/Audience
   - Persistent token (7-day expiry vs 365-day expiry)
   - Non-persistent token (30-minute expiry)
   - Claims generation: Verify all standard and custom claims
   - FullName fallback: constructed from FirstName + LastName when FullName is null/empty
   - `GenerateClaimsWithRights` with null sender throws ArgumentNullException
   - Verify rights claims (Create:SessionEntity, Read:SessionEntity, etc.)

2. Create `ChannelConfigurationValidatorHostedServiceTests.cs` with tests covering:
   - StartAsync validates default channel policies and throws on invalid references
   - StartAsync validates channel bindings and throws on invalid bindings
   - StartAsync normalizes wildcard usage in policy lists
   - StopAsync returns completed task

3. Add tests to `FileSystemAdvancedToolTests.cs` for FileCopyTool:
   - Non-recursive directory copy returns error
   - Recursive directory copy with nested files
   - CreateDirectories=false with nested destination

4. Add tests to existing test files for files with 1 uncovered new line:
   - `GBrainDreamServiceTests.cs`: null payload from MCP call returns Completed with 0 pages
   - IdentityResolver tests: `SplitNonJsonClaimValue` with comma-separated single value
   - `EnrichmentQueue` tests using SQLite in-memory for TryClaimNextAsync and RecoverStaleLeasesAsync

5. Add test for `TextExtractionHelper`: unsupported file type throws InvalidOperationException

6. Add test for `PromptAssembler`: uncovered branch in prompt assembly

7. Add tests for `DiagnosticsCleanupHostedService`: happy path where no invalid bindings exist

8. Add test for `IEndpointRouteBuilderExtensions`: MapOpenAIModels endpoint

9. Run unit tests: `dotnet test test/LeanKernel.Tests.Unit/`

10. Re-run SonarQube scan: `scripts/quality/sonarqube-scan.sh`

11. Verify quality gate status = OK with new_coverage >= 80%

## Review Focus
- Each added test should cover at least one previously uncovered new line
- Total new uncovered lines should decrease by 40+ to reach 80% threshold
- All existing tests must still pass
- No new issues introduced in test code
