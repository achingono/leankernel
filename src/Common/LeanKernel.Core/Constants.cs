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
}