# Phase 24 Activities

## Step-By-Step Activities
1. Analyze how `TenantResolutionMiddleware` handles anonymous fallbacks (Path C).
2. Add `AllowGuestFallback` (default `false`) to `IdentitySettings.cs`.
3. Update `appsettings.Development.json` to set `Identity:AllowGuestFallback: true`.
4. Update `TenantResolutionMiddleware` Path C to:
   - Read `AllowGuestFallback` from `identitySettings`.
   - If `false`, log a warning and return 401 Unauthorized.
   - If `true`, proceed with existing guest-user resolution and log a warning that Path C was entered.
5. Update `TenantResolutionMiddlewareTests`:
   - Modify `InvokeAsync_AnonymousUser_StoresGuestIdentityInItems` to configure `AllowGuestFallback = true`.
   - Add new test `InvokeAsync_AnonymousUser_WhenFallbackDisabled_Returns401`.
6. Update `Phase02BoundaryTests`:
   - Modify `AnonymousRequest_WithoutToken_IsAcceptedWhenNoSigningKeyConfigured` to set `AllowGuestFallback = true` in the test fixture.
   - Add new test `AnonymousRequest_WhenFallbackDisabled_Returns401`.
7. Build, run all tests, verify no regressions.

## Review Focus
- Ensure standard bearer-token requests (Paths A and B) are unaffected.
- Ensure the DevUI is still accessible and functional in development.
- Check that arbitrary production anonymous requests fail closed with a 401.
- Verify structured warning logs are written for both allowed and blocked anonymous paths.
- Confirm configuration schema change is documented.
