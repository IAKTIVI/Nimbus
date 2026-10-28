using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Nimbus.Core;

namespace Nimbus.Integrations;

/// <param name="RootPlaceId">The experience's start place (where Play should go), 0 if unknown.</param>
public sealed record GameDetails(long PlaceId, long UniverseId, string Name, string? Creator, string? IconUrl, long RootPlaceId = 0);

/// <summary>Public Roblox web APIs (no login needed) plus the server location lookup.</summary>
internal static class RobloxApi
{
    private const string LogIdent = "RobloxApi";

    private static readonly ConcurrentDictionary<long, GameDetails> Games = new();
    private static readonly ConcurrentDictionary<string, string> Locations = new();

    /// <summary>Name, creator and icon of the game a place belongs to. Null if Roblox can't be reached.</summary>
    public static async Task<GameDetails?> GetGameAsync(long placeId, CancellationToken ct = default)
    {
        if (Games.TryGetValue(placeId, out var cached))
            return cached;

        try
        {
            using var universe = await GetJsonAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
            long universeId = universe.RootElement.GetProperty("universeId").GetInt64();

            using var games = await GetJsonAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", ct);
            var game = games.RootElement.GetProperty("data")[0];
            string name = game.GetProperty("name").GetString() ?? $"Place {placeId}";
            string? creator = game.TryGetProperty("creator", out var c) ? c.GetProperty("name").GetString() : null;
            long rootPlace = game.TryGetProperty("rootPlaceId", out var root) && root.TryGetInt64(out long r) ? r : 0;

            string? icon = await GetImageUrlAsync(
                $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false", ct);

            return Games[placeId] = new GameDetails(placeId, universeId, name, creator, icon, rootPlace);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or TaskCanceledException)
        {
            Logger.Warn(LogIdent, $"Game details for place {placeId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>An image URL for a Roblox asset id (for statuses games set through BloxstrapRPC).</summary>
    public static Task<string?> GetAssetImageAsync(long assetId, CancellationToken ct = default) =>
        GetImageUrlAsync($"https://thumbnails.roblox.com/v1/assets?assetIds={assetId}&returnPolicy=PlaceHolder&size=512x512&format=Png", ct);

    /// <summary>
    /// "Frankfurt am Main, Germany". Sends only the game server's public IP to ipinfo.io, never anything about the player.
    /// </summary>
    public static async Task<string?> GetServerLocationAsync(string ip, CancellationToken ct = default)
    {
        if (Locations.TryGetValue(ip, out var cached))
            return cached;

        try
        {
            using var info = await GetJsonAsync($"https://ipinfo.io/{Uri.EscapeDataString(ip)}/json", ct);
            var root = info.RootElement;
            string? city = root.TryGetProperty("city", out var c) ? c.GetString() : null;
            string? code = root.TryGetProperty("country", out var cc) ? cc.GetString() : null;

            string? country = code;
            try
            {
                if (code is not null)
                    country = new RegionInfo(code).EnglishName;
            }
            catch (ArgumentException)
            {
            }

            string location = string.Join(", ", new[] { city, country }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return location.Length == 0 ? null : Locations[ip] = location;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Logger.Warn(LogIdent, $"Server location: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> GetImageUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync(url, ct);
            var data = doc.RootElement.GetProperty("data");
            return data.GetArrayLength() > 0 && data[0].TryGetProperty("imageUrl", out var image) ? image.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException)
        {
            Logger.Warn(LogIdent, $"Image lookup: {ex.Message}");
            return null;
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
