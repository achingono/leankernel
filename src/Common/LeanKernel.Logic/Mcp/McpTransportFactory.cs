using LeanKernel.Logic.Configuration;

using ModelContextProtocol.Client;

namespace LeanKernel.Logic.Mcp;

internal static class McpTransportFactory
{
    public static HttpClientTransport Create(McpServerSettings server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.Endpoint),
            TransportMode = ParseTransportMode(server.TransportMode),
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1, server.ConnectionTimeoutSeconds)),
        };

        if (server.AdditionalHeaders.Count > 0)
        {
            options.AdditionalHeaders = new Dictionary<string, string>(server.AdditionalHeaders);
        }

        return new HttpClientTransport(options);
    }

    private static HttpTransportMode ParseTransportMode(string mode)
    {
        if (mode.Equals("Sse", StringComparison.OrdinalIgnoreCase))
        {
            return HttpTransportMode.Sse;
        }

        if (mode.Equals("StreamableHttp", StringComparison.OrdinalIgnoreCase))
        {
            return HttpTransportMode.StreamableHttp;
        }

        return HttpTransportMode.AutoDetect;
    }
}