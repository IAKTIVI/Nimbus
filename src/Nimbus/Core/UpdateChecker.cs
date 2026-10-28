using System.Text.Json;

namespace Nimbus.Core;

/// <param name="DownloadUrl">The release's Nimbus.exe, when it has one (used by auto-update).</param>
public sealed record NimbusRelease(string Version, string Url, string? DownloadUrl = null);

/// <summary>Update check against GitHub Releases. Installing is up to <see cref="SelfUpdater"/> (only when auto-update is on).</summary>
internal static class UpdateChecker
{
    private const string LogIdent = "Updates";

    /// <summary>"owner/name" of Nimbus's GitHub repository; null turns the check off.</summary>
    public static readonly string? Repository = "IAKTIVI/Nimbus";

    public static bool IsConfigured => Repository is not null;

    /// <summary>The latest release if it's newer than this copy, otherwise null (also when offline).</summary>
    public static async Task<NimbusRelease?> CheckAsync(CancellationToken ct = default)
    {
        if (Repository is null)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.Client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases";
            return IsNewer(tag, AppInfo.Version) ? new NimbusRelease(tag.TrimStart('v', 'V'), url, FindExeAsset(doc.RootElement)) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            Logger.Warn(LogIdent, $"Update check failed: {ex.Message}");
            return null;
        }
    }

    internal static string? FindExeAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), "Nimbus.exe", StringComparison.OrdinalIgnoreCase))
                return asset.GetProperty("browser_download_url").GetString();
        }

        return null;
    }

    /// <summary>"v0.6.0" vs "0.5.0" → true. Anything that isn't a version number counts as not newer.</summary>
    internal static bool IsNewer(string tag, string current) =>
        System.Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var latest) &&
        System.Version.TryParse(current.Split('-')[0], out var mine) &&
        latest > mine;
}
