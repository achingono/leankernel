namespace LeanKernel;

/// <summary>
/// Shared string constants used across LeanKernel projects.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Standard and custom claim type names.
    /// </summary>
    public static class Claims
    {
        /// <summary>Claim containing the resolved channel name.</summary>
        public const string Channel = "lk_channel";
        /// <summary>Claim containing the resolved tenant identifier for the channel.</summary>
        public const string ChannelTenantId = "lk_tenant_id";
        /// <summary>Claim containing the sender token issuer.</summary>
        public const string ChannelSenderIssuer = "lk_sender_iss";
        /// <summary>Claim containing the sender token subject.</summary>
        public const string ChannelSenderSubject = "lk_sender_sub";
        /// <summary>JWT issuer claim type.</summary>
        public const string Issuer = "iss";
        /// <summary>JWT subject claim type.</summary>
        public const string Subject = "sub";
        /// <summary>Session identifier claim type.</summary>
        public const string Sid = "sid";
        /// <summary>Email claim type.</summary>
        public const string Email = "email";
        /// <summary>Display name claim type.</summary>
        /// <summary>Preferred username field key.</summary>
        public const string Name = "name";
        /// <summary>Given name claim type.</summary>
        public const string GivenName = "given_name";
        /// <summary>Family name claim type.</summary>
        public const string FamilyName = "family_name";
        /// <summary>User principal name claim type.</summary>
        public const string Upn = "upn";
        /// <summary>Preferred username claim type.</summary>
        public const string PreferredUsername = "preferred_username";
        /// <summary>Locale claim type.</summary>
        /// <summary>Locale field key.</summary>
        public const string Locale = "locale";
        /// <summary>Zone info claim type.</summary>
        /// <summary>Time zone field key.</summary>
        public const string ZoneInfo = "zoneinfo";
        /// <summary>Time zone claim type.</summary>
        public const string TimeZone = "timezone";
        /// <summary>Alternative time zone claim type.</summary>
        /// <summary>Organization field key.</summary>
        public const string TimeZoneAlt = "time_zone";
        /// <summary>Organization claim type.</summary>
        public const string Organization = "organization";
        /// <summary>Alternative organization claim type.</summary>
        /// <summary>Roles field key.</summary>
        public const string OrganizationAlt = "org";
        /// <summary>Company claim type.</summary>
        public const string Company = "company";
        /// <summary>Role claim type.</summary>
        public const string Role = "role";
        /// <summary>Roles claim type.</summary>
        public const string Roles = "roles";
        /// <summary>Group claim type.</summary>
        /// <summary>Groups field key.</summary>
        public const string Group = "group";
        /// <summary>Groups claim type.</summary>
        public const string Groups = "groups";
        /// <summary>XML email claim type used by some identity providers.</summary>
        /// <summary>Custom claims field key.</summary>
        public const string XmlEmailAddress = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress";
        /// <summary>Service URL claim type.</summary>
        public const string ServiceUrl = "serviceurl";
    }

    /// <summary>
    /// Canonical keys used when serializing identity context data.
    /// </summary>
    public static class IdentityContextFields
    {
        /// <summary>Full name field key.</summary>
        public const string FullName = "full_name";
        /// <summary>Email field key.</summary>
        public const string Email = "email";
        public const string PreferredUsername = "preferred_username";
        public const string Locale = "locale";
        public const string TimeZone = "timezone";
        public const string Organization = "organization";
        public const string Roles = "roles";
        public const string Groups = "groups";
        public const string CustomClaims = "custom_claims";
    }

    /// <summary>
    /// Identity-related default values.
    /// </summary>
    public static class Identity
    {
        /// <summary>Issuer used for anonymous identities.</summary>
        public const string AnonymousIssuer = "anonymous";
        /// <summary>System actor display name.</summary>
        public const string SystemName = "System";
        /// <summary>System actor email address.</summary>
        public const string SystemEmail = "system@leankernel.local";
    }

    /// <summary>
    /// Shared HTTP route and content-type constants.
    /// </summary>
    public static class Http
    {
        /// <summary>Health endpoint path.</summary>
        public const string HealthPath = "/health";
        /// <summary>Internal chat-completions proxy endpoint path.</summary>
        public const string InternalCompletionsPath = "/v1/internal/completions";
        /// <summary>JSON media type.</summary>
        public const string ApplicationJson = "application/json";
        /// <summary>Server-sent events media type.</summary>
        public const string TextEventStream = "text/event-stream";
    }

    /// <summary>
    /// HttpContext item keys populated by middleware.
    /// </summary>
    public static class HttpContextItems
    {
        /// <summary>Resolved tenant identifier item key.</summary>
        public const string TenantId = "LK.TenantId";
        /// <summary>Resolved user identifier item key.</summary>
        public const string UserId = "LK.UserId";
        /// <summary>Resolved person identifier item key.</summary>
        public const string PersonId = "LK.PersonId";
        /// <summary>Resolved channel identifier item key.</summary>
        public const string ChannelId = "LK.ChannelId";
        /// <summary>Resolved badge item key.</summary>
        public const string Badge = "LK.Badge";
    }

    /// <summary>
    /// Session-related marker values.
    /// </summary>
    public static class Session
    {
        /// <summary>Metadata marker indicating session initialization has occurred.</summary>
        public const string InitMarker = "_lk_init";
    }

    /// <summary>
    /// Memory tool and payload constants.
    /// </summary>
    public static class GBrain
    {
        public const string SearchTool = "search";
        /// <summary>Get-page tool name.</summary>
        public const string GetPageTool = "get_page";
        /// <summary>Put-page tool name.</summary>
        public const string PutPageTool = "put_page";
        /// <summary>Health probe slug used for read checks.</summary>
        public const string ProbeSlug = "__lk_probe__";
        /// <summary>Health probe slug used for write checks.</summary>
        public const string ProbeWriteSlug = "__lk_probe_write__";
        /// <summary>Default probe content.</summary>
        public const string ProbeContent = "probe";
        /// <summary>Memory root prefix.</summary>
        public const string MemoryPrefix = "memory";
        /// <summary>Default memory source label.</summary>
        public const string Source = "gbrain";
        /// <summary>Results property name.</summary>
        public const string Results = "results";
        /// <summary>Slug property name.</summary>
        public const string Slug = "slug";
        /// <summary>Compiled truth property name.</summary>
        public const string CompiledTruth = "compiled_truth";
        /// <summary>Chunk text property name.</summary>
        public const string ChunkText = "chunk_text";
        /// <summary>Content property name.</summary>
        public const string Content = "content";
        /// <summary>Title property name.</summary>
        public const string Title = "title";
        /// <summary>Score property name.</summary>
        public const string Score = "score";
    }

    /// <summary>
    /// Turn-runtime context and admission reason constants.
    /// </summary>
    public static class TurnRuntime
    {
        /// <summary>
        /// Known context source names used during prompt assembly.
        /// </summary>
        public static class ContextSource
        {
            /// <summary>System context source.</summary>
            public const string System = "system";
            /// <summary>Identity context source.</summary>
            public const string Identity = "identity";
            /// <summary>Memory context source.</summary>
            public const string Memory = "memory";
            /// <summary>Retrieval context source.</summary>
            public const string Retrieval = "retrieval";
            /// <summary>History context source.</summary>
            public const string History = "history";
        }

        /// <summary>
        /// Reasons explaining context admission and rejection decisions.
        /// </summary>
        public static class AdmissionReason
        {
            /// <summary>Item is admitted because it is required system context.</summary>
            public const string SystemContext = "system_context";
            /// <summary>Item is rejected because the system budget is exhausted.</summary>
            public const string SystemBudgetExhausted = "system_budget_exhausted";
            /// <summary>Item is rejected because total budget is exhausted.</summary>
            public const string BudgetExhausted = "budget_exhausted";
            /// <summary>Item is rejected because candidate limit was reached.</summary>
            public const string MaxCandidatesReached = "max_candidates_reached";
            /// <summary>Item is rejected for low score.</summary>
            public const string LowScore = "low_score";
            /// <summary>Item is admitted by ranking and budget checks.</summary>
            public const string Admitted = "admitted";
        }
    }
}
#pragma warning restore CS1591
#pragma warning disable CS1591
