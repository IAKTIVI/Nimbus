using System.Net;
using System.Net.Http.Headers;

namespace Nimbus.Core;

internal static class Http
{
    /// <summary>One shared client so connections to Roblox's CDN are pooled and reused.</summary>
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version));
        return client;
    }
}
