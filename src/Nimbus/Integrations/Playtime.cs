using Nimbus.Core;

namespace Nimbus.Integrations;

/// <summary>Total time played in one game (all its places together), for "Most played" on the History page.</summary>
public sealed class PlaytimeEntry
{
    public long PlaceId { get; set; }

    public string Name { get; set; } = "";

    public string? IconUrl { get; set; }

    public long Seconds { get; set; }

    public int Sessions { get; set; }

    public DateTimeOffset LastPlayed { get; set; }
}

/// <summary>
/// Playtime per game (Playtime.json). Unlike History it never drops old sessions, so totals keep growing.
/// Reloaded on every change, since several Nimbus processes may write it.
/// </summary>
internal static class Playtime
{
    private const string LogIdent = "Playtime";

    /// <summary>Shorter visits (a quick look, a failed join) don't count as a session.</summary>
    private static readonly TimeSpan MinimumSession = TimeSpan.FromSeconds(30);

    /// <summary>A missed "left the game" could otherwise add days; no real session is this long.</summary>
    private static readonly TimeSpan MaximumSession = TimeSpan.FromHours(24);

    private static string FilePath => Path.Combine(Paths.Base, "Playtime.json");

    /// <summary>The key a game is counted under: its experience when known, so teleports between places add up.</summary>
    internal static string Key(long placeId, long? universeId) => universeId is > 0 ? $"u{universeId}" : $"p{placeId}";

    private static JsonStore<Dictionary<string, PlaytimeEntry>> Open()
    {
        bool fresh = !File.Exists(FilePath);
        var store = new JsonStore<Dictionary<string, PlaytimeEntry>>(FilePath, NimbusJson.Default.DictionaryStringPlaytimeEntry);
        store.Load();
        if (fresh)
            ImportHistory(store.Value, GameHistory.Load()); // a head start from the games already in History
        return store;
    }

    /// <summary>Most played first.</summary>
    public static IReadOnlyList<PlaytimeEntry> Top(int count) =>
        Open().Value.Values.OrderByDescending(e => e.Seconds).Take(count).ToList();

    public static void Add(long placeId, long? universeId, string name, string? iconUrl, DateTimeOffset joined, DateTimeOffset left)
    {
        try
        {
            var store = Open();
            if (Record(store.Value, Key(placeId, universeId), placeId, name, iconUrl, joined, left))
                store.Save();
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, ex.Message);
        }
    }

    /// <summary>
    /// Totals counted per place (from History, or while Roblox couldn't be reached) are merged into their whole
    /// experience, so a game with a lobby and a match place shows once. Returns true if anything changed.
    /// </summary>
    public static async Task<bool> MergeByExperienceAsync()
    {
        var places = Open().Value.Where(e => e.Key.StartsWith('p')).Select(e => e.Value.PlaceId).Distinct().ToList();
        if (places.Count == 0)
            return false;

        var experiences = new Dictionary<long, GameDetails>();
        foreach (long place in places)
        {
            if (await RobloxApi.GetGameAsync(place) is { UniverseId: > 0 } game)
                experiences[place] = game;
        }
        if (experiences.Count == 0)
            return false;

        try
        {
            var store = Open();
            if (!Merge(store.Value, experiences))
                return false;
            store.Save();
            return true;
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, ex.Message);
            return false;
        }
    }

    /// <summary>Moves place totals into their experience's total (adding them up). True if anything moved.</summary>
    internal static bool Merge(Dictionary<string, PlaytimeEntry> totals, IReadOnlyDictionary<long, GameDetails> experiences)
    {
        bool changed = false;
        foreach (var (key, entry) in totals.Where(e => e.Key.StartsWith('p')).ToList())
        {
            if (!experiences.TryGetValue(entry.PlaceId, out var game))
                continue;

            string target = Key(entry.PlaceId, game.UniverseId);
            if (!totals.TryGetValue(target, out var whole))
                totals[target] = whole = new PlaytimeEntry();

            whole.Seconds += entry.Seconds;
            whole.Sessions += entry.Sessions;
            if (entry.LastPlayed > whole.LastPlayed)
                whole.LastPlayed = entry.LastPlayed;
            whole.PlaceId = game.RootPlaceId > 0 ? game.RootPlaceId : whole.PlaceId == 0 ? entry.PlaceId : whole.PlaceId;
            whole.Name = game.Name;
            whole.IconUrl = game.IconUrl ?? entry.IconUrl ?? whole.IconUrl;
            totals.Remove(key);
            changed = true;
        }
        return changed;
    }

    /// <summary>Takes one game out of the totals (all entries counted under its place).</summary>
    public static void Remove(long placeId)
    {
        try
        {
            var store = Open();
            if (Remove(store.Value, placeId))
                store.Save();
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, ex.Message);
        }
    }

    internal static bool Remove(Dictionary<string, PlaytimeEntry> totals, long placeId)
    {
        var keys = totals.Where(e => e.Value.PlaceId == placeId).Select(e => e.Key).ToList();
        foreach (string key in keys)
            totals.Remove(key);
        return keys.Count > 0;
    }

    public static void Clear()
    {
        try
        {
            File.Delete(FilePath);
            // Written empty, so the next start doesn't import the (now also cleared) history again.
            new JsonStore<Dictionary<string, PlaytimeEntry>>(FilePath, NimbusJson.Default.DictionaryStringPlaytimeEntry).Save();
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, ex.Message);
        }
    }

    /// <summary>Adds one session to the totals. False if it was too short (or nonsense) to count.</summary>
    internal static bool Record(Dictionary<string, PlaytimeEntry> totals, string key, long placeId, string name, string? iconUrl,
        DateTimeOffset joined, DateTimeOffset left)
    {
        var length = left - joined;
        if (length < MinimumSession || length > MaximumSession)
            return false;

        if (!totals.TryGetValue(key, out var entry))
            totals[key] = entry = new PlaytimeEntry { PlaceId = placeId };

        entry.Seconds += (long)length.TotalSeconds;
        entry.Sessions++;
        if (left >= entry.LastPlayed)
        {
            entry.LastPlayed = left;
            entry.Name = name;
            entry.IconUrl = iconUrl ?? entry.IconUrl;
        }
        return true;
    }

    /// <summary>History entries don't know the experience, so they're counted by place.</summary>
    internal static void ImportHistory(Dictionary<string, PlaytimeEntry> totals, IEnumerable<HistoryEntry> history)
    {
        foreach (var h in history)
        {
            if (h.LeftAt is { } left)
                Record(totals, Key(h.PlaceId, null), h.PlaceId, h.Name, h.IconUrl, h.JoinedAt, left);
        }
    }

    /// <summary>"3 h 20 min", "45 min", "under a minute".</summary>
    public static string Format(long seconds)
    {
        long minutes = seconds / 60;
        return minutes < 1 ? Loc.T("Stats.UnderMinute")
            : minutes < 60 ? Loc.T("Stats.Minutes", minutes)
            : Loc.T("Stats.Hours", minutes / 60, minutes % 60);
    }
}
