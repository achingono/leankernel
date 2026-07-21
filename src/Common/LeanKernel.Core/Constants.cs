using System.Text.Json;

namespace LeanKernel;

/// <summary>
/// Contains constant values used throughout the LeanKernel application.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Contains constants related to the default agent configuration.
    /// </summary>
    public static class Agent
    {
        /// <summary>
        /// The default name of the agent.
        /// </summary>
        public const string DefaultName = "leankernel";
    }

    /// <summary>
    /// Contains constants related to HTTP headers used in requests and responses.
    /// </summary>
    public static class Http
    {
        /// <summary>
        /// The name of the HTTP header used for authorization.
        /// </summary>
        public static class Headers
        {
            /// <summary>
            /// The name of the HTTP header used for authorization.
            /// </summary>
            public const string Bearer = "Bearer";
        }
    }

    /// <summary>
    /// Contains constants related to content types used in HTTP requests and responses.
    /// </summary>
    public static class ContentTypes
    {
        /// <summary>
        /// The content type for JSON data.
        /// </summary>
        public const string Json = "application/json";

        /// <summary>
        /// The content type for plain text data.
        /// </summary>
        public const string PlainText = "text/plain";
    }

    /// <summary>
    /// Contains constants related to health check endpoints.
    /// </summary>
    public static class Healthchecks
    {
        /// <summary>
        /// The path for the health check endpoint.
        /// </summary>
        public const string Path = "/health";

        /// <summary>
        /// The name of the health check for the database.
        /// </summary>
        public const string Database = "database";

        /// <summary>
        /// The name of the health check for the gateway.
        /// </summary>
        public const string Gateway = "gateway";
    }

    /// <summary>
    /// Contains constants related to database connection strings.
    /// </summary>
    public static class ConnectionStrings
    {
        /// <summary>
        /// The name of the connection string for SQL Server.
        /// </summary>
        public const string SqlServer = "SqlServer";

        /// <summary>
        /// The name of the connection string for PostgreSQL.
        /// </summary>
        public const string Postgres = "Postgres";

        /// <summary>
        /// The name of the connection string for SQLite.
        /// </summary>
        public const string Sqlite = "Sqlite";

        /// <summary>
        /// An array containing all supported connection string names.
        /// </summary>
        public static readonly string[] All = [SqlServer, Postgres, Sqlite];
    }

    /// <summary>
    /// Contains constants related to serialization settings.
    /// </summary>
    public static class Serialization
    {
        /// <summary>
        /// The default <see cref="JsonSerializerOptions"/> used for JSON serialization and deserialization.
        /// This uses the web defaults for property naming and other settings.
        /// </summary>
        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// Identity-related constants.
    /// </summary>
    public static class Identity
    {
        /// <summary>
        /// The issuer value assigned to anonymous/guest users.
        /// </summary>
        public const string AnonymousIssuer = "anonymous";
    }

    /// <summary>
    /// Claim type constants used for identity resolution.
    /// </summary>
    public static class Claims
    {
        /// <summary>
        /// Issuer claim type.
        /// </summary>
        public const string Issuer = "iss";

        /// <summary>
        /// Subject claim type.
        /// </summary>
        public const string Subject = "sub";

        /// <summary>
        /// Preferred username claim type.
        /// </summary>
        public const string PreferredUsername = "preferred_username";

        /// <summary>
        /// Email claim type.
        /// </summary>
        public const string Email = "email";

        /// <summary>
        /// XML email address claim type.
        /// </summary>
        public const string XmlEmailAddress = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress";

        /// <summary>
        /// Given name claim type.
        /// </summary>
        public const string GivenName = "given_name";

        /// <summary>
        /// Family name claim type.
        /// </summary>
        public const string FamilyName = "family_name";

        /// <summary>
        /// Name claim type.
        /// </summary>
        public const string Name = "name";

        /// <summary>
        /// UPN claim type.
        /// </summary>
        public const string Upn = "upn";

        /// <summary>
        /// Locale claim type.
        /// </summary>
        public const string Locale = "locale";

        /// <summary>
        /// Zoneinfo claim type.
        /// </summary>
        public const string ZoneInfo = "zoneinfo";

        /// <summary>
        /// Timezone claim type.
        /// </summary>
        public const string TimeZone = "timezone";

        /// <summary>
        /// Alternative timezone claim type.
        /// </summary>
        public const string TimeZoneAlt = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/timezone";

        /// <summary>
        /// Organization claim type.
        /// </summary>
        public const string Organization = "organization";

        /// <summary>
        /// Alternative organization claim type.
        /// </summary>
        public const string OrganizationAlt = "org";

        /// <summary>
        /// Company claim type.
        /// </summary>
        public const string Company = "company";

        /// <summary>
        /// Role claim type.
        /// </summary>
        public const string Role = "role";

        /// <summary>
        /// Roles claim type.
        /// </summary>
        public const string Roles = "roles";

        /// <summary>
        /// Groups claim type.
        /// </summary>
        public const string Groups = "groups";

        /// <summary>
        /// Group claim type.
        /// </summary>
        public const string Group = "group";

        /// <summary>
        /// Channel sender issuer claim type.
        /// </summary>
        public const string ChannelSenderIssuer = "lk_sender_iss";

        /// <summary>
        /// Channel sender subject claim type.
        /// </summary>
        public const string ChannelSenderSubject = "lk_sender_sub";
    }

    /// <summary>
    /// Identity context field names used for prompt rendering and configuration.
    /// </summary>
    public static class IdentityContextFields
    {
        /// <summary>
        /// Full name field.
        /// </summary>
        public const string FullName = "full_name";

        /// <summary>
        /// Email field.
        /// </summary>
        public const string Email = "email";

        /// <summary>
        /// Preferred username field.
        /// </summary>
        public const string PreferredUsername = "preferred_username";

        /// <summary>
        /// Locale field.
        /// </summary>
        public const string Locale = "locale";

        /// <summary>
        /// Time zone field.
        /// </summary>
        public const string TimeZone = "timezone";

        /// <summary>
        /// Organization field.
        /// </summary>
        public const string Organization = "organization";

        /// <summary>
        /// Roles field.
        /// </summary>
        public const string Roles = "roles";

        /// <summary>
        /// Groups field.
        /// </summary>
        public const string Groups = "groups";

        /// <summary>
        /// Custom claims field.
        /// </summary>
        public const string CustomClaims = "custom_claims";
    }

    /// <summary>
    /// Turn-runtime constants for source labels and admission reasons.
    /// </summary>
    public static class TurnRuntime
    {
        /// <summary>
        /// Context source labels.
        /// </summary>
        public static class ContextSource
        {
            /// <summary>
            /// System source label.
            /// </summary>
            public const string System = "system";

            /// <summary>
            /// Identity source label.
            /// </summary>
            public const string Identity = "identity";

            /// <summary>
            /// Memory source label.
            /// </summary>
            public const string Memory = "memory";

            /// <summary>
            /// Retrieval source label.
            /// </summary>
            public const string Retrieval = "retrieval";
        }

        /// <summary>
        /// Admission reason labels.
        /// </summary>
        public static class AdmissionReason
        {
            /// <summary>
            /// System context admitted.
            /// </summary>
            public const string SystemContext = "system_context";

            /// <summary>
            /// System budget exhausted.
            /// </summary>
            public const string SystemBudgetExhausted = "system_budget_exhausted";

            /// <summary>
            /// Budget exhausted.
            /// </summary>
            public const string BudgetExhausted = "budget_exhausted";

            /// <summary>
            /// Max candidates reached.
            /// </summary>
            public const string MaxCandidatesReached = "max_candidates_reached";

            /// <summary>
            /// Low score rejection.
            /// </summary>
            public const string LowScore = "low_score";

            /// <summary>
            /// Item admitted.
            /// </summary>
            public const string Admitted = "admitted";
        }
    }
}