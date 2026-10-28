using System.Diagnostics;
using Nimbus.Core;

namespace Nimbus.Performance;

public enum CleanupKind
{
    /// <summary>Roblox's log files (%LocalAppData%\Roblox\logs).</summary>
    Logs,

    /// <summary>Roblox's temporary files (%Temp%\Roblox).</summary>
    TempFiles,

    /// <summary>What Roblox's own installer downloaded for the player and Studio (%LocalAppData%\Roblox\Downloads).</summary>
    InstallerDownloads,

    /// <summary>Roblox's downloaded game assets (rbx-storage); re-downloaded as you play.</summary>
    AssetCache,

    /// <summary>Package downloads left over in Nimbus's own folder.</summary>
    NimbusDownloads,
}

public sealed record CleanupResult(long FreedBytes, int SkippedFiles);

/// <summary>Frees disk space Roblox leaves behind. Files that are in use are skipped, never forced.</summary>
internal static class Cleaner
{
    private const string LogIdent = "Cleaner";

    /// <summary>"Clean on launch" only removes logs older than this, so recent ones stay for troubleshooting.</summary>
    public static readonly TimeSpan OldLogAge = TimeSpan.FromDays(3);

    /// <summary>Tests point these at temporary folders so they never touch the real Roblox data.</summary>
    internal static string? DataFolderOverride { get; set; }

    internal static string? TempFolderOverride { get; set; }

    private static string RobloxData => DataFolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    private static string RobloxTemp => TempFolderOverride ?? Path.Combine(Path.GetTempPath(), "Roblox");

    /// <summary>These are only safe to delete while neither Roblox nor Roblox Studio is open.</summary>
    public static bool NeedsRobloxClosed(CleanupKind kind) => kind is CleanupKind.AssetCache or CleanupKind.InstallerDownloads;

    public static bool IsRobloxOrStudioRunning()
    {
        foreach (string name in new[] { "RobloxPlayerBeta", "RobloxStudioBeta" })
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes)
                p.Dispose();
            if (processes.Length > 0)
                return true;
        }

        return false;
    }

    public static long Measure(CleanupKind kind, TimeSpan? olderThan = null) =>
        Files(kind, olderThan).Sum(f => SafeLength(f));

    public static CleanupResult Clean(CleanupKind kind, TimeSpan? olderThan = null)
    {
        if (NeedsRobloxClosed(kind) && IsRobloxOrStudioRunning())
            throw new InvalidOperationException(Loc.T("Performance.CloseRobloxFirst"));

        long freed = 0;
        int skipped = 0;
        foreach (var file in Files(kind, olderThan).ToList())
        {
            try
            {
                long size = file.Length;
                file.Delete();
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // in use right now
            }
        }

        foreach (string root in Roots(kind))
            RemoveEmptyFolders(root);

        Logger.Info(LogIdent, $"{kind}: freed {freed / 1048576.0:F1} MB" + (skipped > 0 ? $", {skipped} files in use were skipped" : ""));
        return new CleanupResult(freed, skipped);
    }

    /// <summary>The light clean-up that runs before each launch when "Clean on every launch" is on.</summary>
    public static void CleanBeforeLaunch()
    {
        try
        {
            Clean(CleanupKind.Logs, OldLogAge);
            Clean(CleanupKind.TempFiles);
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Clean-up before launch skipped: {ex.Message}");
        }
    }

    private static IEnumerable<string> Roots(CleanupKind kind) => kind switch
    {
        CleanupKind.Logs => [Path.Combine(RobloxData, "logs")],
        CleanupKind.TempFiles => [RobloxTemp],
        CleanupKind.InstallerDownloads => [Path.Combine(RobloxData, "Downloads")],
        CleanupKind.AssetCache => [Path.Combine(RobloxData, "rbx-storage")],
        CleanupKind.NimbusDownloads => [Paths.Downloads],
        _ => [],
    };

    private static IEnumerable<FileInfo> Files(CleanupKind kind, TimeSpan? olderThan)
    {
        var files = Roots(kind)
            .Where(Directory.Exists)
            .SelectMany(root => new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }));

        // The asset cache also keeps an index database next to its folder.
        if (kind == CleanupKind.AssetCache)
        {
            files = files.Concat(new[] { "rbx-storage.db", "rbx-storage.db-shm", "rbx-storage.db-wal" }
                .Select(name => new FileInfo(Path.Combine(RobloxData, name)))
                .Where(f => f.Exists));
        }

        if (kind == CleanupKind.Logs)
            files = files.Where(f => f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase));

        if (olderThan is { } age)
            files = files.Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > age);

        return files;
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch (IOException) { return 0; }
    }

    private static void RemoveEmptyFolders(string root)
    {
        if (!Directory.Exists(root))
            return;

        foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
