## Plan: Unified LeanKernel Chat History

LeanKernel currently creates a browser-local owner id in the Blazor chat page and stores sessions by `(ChannelId, UserId)`, so the same authenticated person gets different `UserId` values in different browsers. The recommended fix is to make the Gateway create an authenticated `ClaimsPrincipal` from oauth2-proxy’s trusted forwarded session headers, use a stable claim-derived user key everywhere chat sessions are created or queried, and run a one-time best-effort migration from the old localStorage owner id to the authenticated user id.

**Steps**
1. Add a Gateway authentication layer for trusted oauth2-proxy headers. Create a small forwarded-auth authentication handler in the LeanKernel source repo that reads `X-Auth-Request-User`, `X-Auth-Request-Email`, and, if safely parseable, the `Authorization: Bearer` JWT claims. Normalize a stable user key with this precedence: OIDC `sub` when available, then email, then forwarded user. Add config such as `LeanKernel:Auth:ForwardedHeaders:Enabled` and `LeanKernel:Auth:ForwardedHeaders:RequireAuthenticatedUser` so local development can remain controllable.
2. Wire auth into the Gateway composition root. In `Program.cs`, register authentication/authorization, add `AddCascadingAuthenticationState` for Blazor, add `UseAuthentication()` and `UseAuthorization()` before endpoint/component mapping, keep `/api/health` and `/healthz` public, and require authorization for the Blazor app and protected API routes.
3. Replace browser-local owner identity in the Blazor chat. In `Components/Pages/Chat.razor`, stop generating a new GUID for the primary owner id. Instead resolve the authenticated user key from `AuthenticationStateProvider` or an injected identity accessor, pass that into `ChatService.InitializeAsync`, and keep the old `leankernel.chat.owner-id` only as a legacy migration source.
4. User-scope or remove browser session cache. The existing `leankernel.chat.sessions` localStorage cache can briefly show the wrong account/session list on a reused browser profile. Replace it with a user-scoped cache key derived from the authenticated user key, or remove the cache and rely on the database-backed `RefreshSessionsAsync` path.
5. Add best-effort legacy migration. Before the first authenticated `RefreshSessionsAsync`, if the old localStorage owner id exists and differs from the authenticated user key, update existing `engine."Sessions"` rows where `UserId == legacyOwnerId` and `ChannelId LIKE 'blazor:%'` to the authenticated user key. Make this idempotent, log count migrated, tolerate unique-index races, and clear or mark the legacy localStorage owner after success.
6. Harden session ownership checks. Update `ChatService.RefreshSessionsAsync`, `ResolveChannelIdAsync`, and `OpenSessionAsync` so a session must belong to the authenticated `OwnerId`; fail closed with a not-found/unauthorized state instead of allowing arbitrary session ids from URLs. Keep the current `blazor:` channel filter for the UI session rail.
7. Protect `/api/chat` with authenticated identity too. Update `Endpoints.HandleChatAsync` so successful API-key validation is no longer enough by itself when auth is required. Derive `SenderId` from the authenticated user key, ignore or reject caller-supplied `UserId`, default `ChannelId` to `api` unless an allowed channel is supplied, and verify any supplied `SessionId` belongs to that authenticated user before running the turn.
8. Extend persistence APIs deliberately. Either add narrowly named methods to `ISessionStore`/`PostgresSessionStore` such as `SessionBelongsToUserAsync` and `MigrateUserSessionsAsync`, or introduce a Gateway-owned session query/migration service using `LeanKernelDbContext`. Prefer the latter if the operations are UI/API ownership concerns rather than core turn-pipeline persistence.
9. Add schema initialization only if needed. This plan can reuse the existing `UserId` column and unique `(ChannelId, UserId)` index, so no new column is required. If implementation chooses to track legacy ids in metadata, keep it additive and update `LeanKernelDbContextSchemaExtensions` with `CREATE INDEX IF NOT EXISTS`/`ALTER TABLE IF NOT EXISTS` style SQL rather than relying only on generated EF migrations, because production startup currently performs targeted schema creation.
10. Update deployment config for explicit header propagation. In the swarm repo, confirm `deploy/leankernel/docker-stack.yml` keeps `OAUTH2_PROXY_SET_XAUTHREQUEST`, `OAUTH2_PROXY_PASS_ACCESS_TOKEN`, and `OAUTH2_PROXY_SET_AUTHORIZATION_HEADER`; add explicit user/header options if the tested oauth2-proxy version requires them. Add Gateway auth env vars to the `engine` service, and bump the immutable Docker config name if `engine-entrypoint.sh` changes.
11. Update docs. Document the new auth/session behavior in `docs/deployment/stacks/leankernel.md`: oauth2-proxy provides the browser session, Gateway derives the stable user key, browser-local GUIDs are legacy only, and history is unified by authenticated user across browsers.
12. Deploy via the repo workflow. Build LeanKernel images from the source repo through the swarm scripts, deploy with `./deploy/leankernel/scripts/deploy.sh --build --build-tag <unique-tag>`, then force-update services if config mounts changed.

**Relevant files**
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Gateway/Program.cs` — register and enforce authentication/authorization, add Blazor auth state.
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Gateway/Components/Pages/Chat.razor` — replace local GUID owner id with authenticated user key; retain old key only for migration.
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Gateway/Services/ChatService.cs` — initialize, list, open, create, and send sessions using authenticated `OwnerId`; add migration call and ownership checks.
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Gateway/Endpoints.cs` — make `/api/chat` use authenticated identity, reject spoofed user ids, and verify session ownership.
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Gateway/Models/ChatRequest.cs` — consider deprecating `UserId` or documenting that it is ignored when auth is enabled.
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Persistence/PostgresSessionStore.cs` — add ownership/migration helpers only if not implemented as a Gateway service.
- `/Users/achingono/source/repos/leankernel/src/LeanKernel.Persistence/Entities/SessionEntity.cs` and `/Users/achingono/source/repos/leankernel/src/LeanKernel.Persistence/LeanKernelDbContext.cs` — likely no schema change; touch only if metadata/index changes are chosen.
- `/Users/achingono/source/repos/leankernel/test/LeanKernel.Tests.Unit/Persistence/PostgresSessionStoreTests.cs` — cover any new persistence helper behavior.
- `/Users/achingono/source/repos/leankernel/test/LeanKernel.Tests.Integration/GatewayEndpointTests.cs` — update `/api/chat` tests for authenticated identity and spoofed `UserId` rejection/ignore behavior.
- `deploy/leankernel/docker-stack.yml` — ensure oauth2-proxy forwards identity headers and engine receives auth config.
- `deploy/leankernel/config/engine-entrypoint.sh` — only if auth config/secrets need runtime export.
- `docs/deployment/stacks/leankernel.md` — document the unified history behavior and operational checks.

**Verification**
1. In the LeanKernel source repo, run unit and integration tests: `dotnet test src/LeanKernel.sln`.
2. Add/verify tests where two simulated authenticated requests with the same claim but different legacy owner ids see the same Blazor session list after migration.
3. Add/verify tests where two different authenticated users cannot open each other’s `/chat/{sessionId}` URL and cannot send `/api/chat` to another user’s `SessionId`.
4. Add/verify `/api/chat` tests where a request supplies `UserId = attacker` but authenticated claims resolve to `user-a`; the resulting `LeanKernelMessage.SenderId` must be `user-a`.
5. After deployment, use the required swarm workflow: `./deploy/leankernel/scripts/deploy.sh --build --build-tag <unique-tag>` from the swarm repo, then inspect `DOCKER_HOST="ssh://192.168.1.5" docker stack services leankernel` and `DOCKER_HOST="ssh://192.168.1.5" docker service logs leankernel_engine --tail 100`.
6. Manual browser validation: sign in as the same user in two different browsers, create a chat session in browser A, refresh browser B, and confirm the session appears and opens with the same turns.
7. Manual migration validation: use a browser profile that already has `leankernel.chat.owner-id` and old sessions, sign in after the deploy, confirm those sessions appear, then confirm a second browser for the same account sees them too.
8. Negative validation: sign in as a different user and confirm the first user’s sessions do not appear and direct `/chat/{sessionId}` navigation is denied or shown as not found.

**Decisions**
- Auth applies to both the Blazor chat UI and `/api/chat`.
- Existing browser-local sessions should be migrated best-effort into the authenticated user’s history.
- Use the existing `SessionEntity.UserId` as the stable authenticated user key; do not add a new user/session table unless implementation uncovers a stronger need.
- Prefer oauth2-proxy forwarded session headers over adding a second full OIDC login flow inside the Gateway, because the deployment already uses oauth2-proxy as the public auth boundary.
- Keep UI history scoped to `blazor:` sessions; `/api/chat` may keep its own `api` channel unless a future product decision says API sessions should appear in the browser rail.

**Further Considerations**
1. Stable claim choice: prefer `sub` for immutability if available from the forwarded token; otherwise use normalized email because oauth2-proxy already forwards it reliably.
2. Header trust boundary: this is safe only because `engine` is not publicly routed directly. If a direct public route to `engine` is ever added, forwarded-auth headers must be stripped/revalidated at the edge or replaced with full token validation.
3. Multi-account same browser: user-scoped session cache avoids stale session flashes when different people use the same browser profile.