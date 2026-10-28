using Nimbus.Core;

namespace Nimbus.Integrations;

/// <summary>Recently played games and servers (History.json). Reloaded on every change, since several Nimbus processes may write it.</summary>
internal static class GameHistory
{
    public const int MaxEntries = 50;

    private static string FilePath => Path.Combine(Paths.Base, "History.json");

    private static JsonStore<List<HistoryEntry>> Open()
    {
        var store = new JsonStore<List<HistoryEntry>>(FilePath, NimbusJson.Default.ListHistoryEntry);
        store.Load();
        return store;
    }

    public static IReadOnlyList<HistoryEntry> Load() => Open().Value;

    public static void Add(HistoryEntry entry) => Change(list =>
    {
        list.Insert(0, entry);
        if (list.Count > MaxEntries)
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);
    });

    /// <summary>Updates the newest entry for this server (e.g. once its name or location is known, or when it ends).</summary>
    public static void Update(string jobId, Action<HistoryEntry> change) => Change(list =>
    {
        if (list.FirstOrDefault(e => e.JobId == jobId) is { } entry)
            change(entry);
    });

    public static void Clear() => Change(list => list.Clear());

    private static void Change(Action<List<HistoryEntry>> change)
    {
        try
        {
            var store = Open();
            change(store.Value);
            store.Save();
        }
        catch (IOException ex)
        {
            Logger.Warn("History", ex.Message);
        }
    }

    public static string RejoinLink(long placeId, string jobId) => $"roblox://experiences/start?placeId={placeId}&gameInstanceId={jobId}";

    public static string PlayLink(long placeId) => $"roblox://experiences/start?placeId={placeId}";
}
