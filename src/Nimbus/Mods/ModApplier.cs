using System.Text.Json;
using Nimbus.Core;

namespace Nimbus.Mods;

/// <summary>What should happen to one file inside the Roblox version folder.</summary>
internal abstract record ModFile
{
    /// <summary>Replace (or add) the file with a copy of <paramref name="Source"/>.</summary>
    public sealed record Copy(string Source) : ModFile;

    /// <summary>Replace (or add) the file with these bytes.</summary>
    public sealed record Write(byte[] Content) : ModFile;

    /// <summary>Remove the file.</summary>
    public sealed record Delete : ModFile;
}

public sealed record ModResult(int Changed, int Restored, int Failed);

/// <summary>
/// Applies a set of file mods to one Roblox version folder. Before a file is touched for the first time, Roblox's
/// original is copied to the backup folder, so a mod that's turned off later gets the original put back.
/// </summary>
internal sealed class ModApplier(string versionDirectory, string backupDirectory)
{
    private const string LogIdent = "Mods";

    private Dictionary<string, bool>? _applied;

    private string StateFile => Path.Combine(backupDirectory, "applied.json");

    /// <summary>relative path -> whether Roblox shipped that file (so restoring means copying it back, not deleting).</summary>
    private Dictionary<string, bool> Applied => _applied ??= LoadApplied();

    /// <summary>Normalises "content/sounds/x.ogg" and "\content\sounds\x.ogg" to "content\sounds\x.ogg".</summary>
    public static string Normalize(string relativePath) => relativePath.Replace('/', '\\').TrimStart('\\');

    /// <summary>
    /// Roblox's own version of a file, whether or not a mod has replaced it. Null if Roblox doesn't ship it.
    /// </summary>
    public byte[]? ReadOriginal(string relativePath)
    {
        relativePath = Normalize(relativePath);

        if (Applied.TryGetValue(relativePath, out bool existed))
        {
            string backup = Resolve(backupDirectory, relativePath);
            return existed && File.Exists(backup) ? File.ReadAllBytes(backup) : null;
        }

        string current = Resolve(versionDirectory, relativePath);
        return File.Exists(current) ? File.ReadAllBytes(current) : null;
    }

    /// <summary>Roblox's original files in a folder (relative paths), including ones a mod deleted or replaced.</summary>
    public IReadOnlyList<string> ListOriginals(string relativeFolder, string pattern = "*")
    {
        relativeFolder = Normalize(relativeFolder).TrimEnd('\\');
        var applied = Applied;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string current = Resolve(versionDirectory, relativeFolder);
        if (Directory.Exists(current))
        {
            foreach (string file in Directory.EnumerateFiles(current, pattern))
            {
                string rel = Path.GetRelativePath(versionDirectory, file);
                if (!applied.TryGetValue(rel, out bool existed) || existed)
                    files.Add(rel);
            }
        }

        string backup = Resolve(backupDirectory, relativeFolder);
        if (Directory.Exists(backup))
        {
            foreach (string file in Directory.EnumerateFiles(backup, pattern))
                files.Add(Path.GetRelativePath(backupDirectory, file));
        }

        return files.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Makes the version folder match <paramref name="mods"/>; anything modded before but not listed now is restored.</summary>
    public ModResult Apply(IReadOnlyDictionary<string, ModFile> mods)
    {
        var wanted = new Dictionary<string, ModFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, mod) in mods)
            wanted[Normalize(path)] = mod;

        var applied = Applied;
        int changed = 0, restored = 0, failed = 0, tracked = 0;

        // 1. Put back the originals of mods that were turned off.
        foreach (string rel in applied.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            if (TryRun(rel, () => Restore(rel, applied[rel])))
            {
                applied.Remove(rel);
                restored++;
            }
            else
            {
                failed++;
            }
        }

        // 2. Back up every original before anything is changed, and record it first,
        //    so a crash halfway can never make a modded file look like Roblox's original.
        foreach (string rel in wanted.Keys.Where(k => !applied.ContainsKey(k)).ToList())
        {
            bool existed = false;
            bool backedUp = TryRun(rel, () =>
            {
                string target = Resolve(versionDirectory, rel);
                existed = File.Exists(target);
                if (existed)
                    CopyFile(target, Resolve(backupDirectory, rel));
            });

            if (!backedUp)
            {
                wanted.Remove(rel);
                failed++;
                continue;
            }

            applied[rel] = existed;
            tracked++;
        }

        if (restored + tracked > 0)
            SaveApplied(applied);

        // 3. Apply the mods.
        foreach (var (rel, mod) in wanted)
        {
            bool didChange = false;
            if (TryRun(rel, () => didChange = ApplyOne(Resolve(versionDirectory, rel), mod)))
            {
                if (didChange)
                    changed++;
            }
            else
            {
                failed++;
            }
        }

        if (changed + restored + failed > 0)
            Logger.Info(LogIdent, $"{Path.GetFileName(versionDirectory)}: {changed} files modded, {restored} restored, {failed} failed ({wanted.Count} mods active)");

        return new ModResult(changed, restored, failed);
    }

    private static bool ApplyOne(string target, ModFile mod)
    {
        switch (mod)
        {
            case ModFile.Copy copy:
                var source = new FileInfo(copy.Source);
                var existing = new FileInfo(target);
                if (existing.Exists && existing.Length == source.Length && existing.LastWriteTimeUtc == source.LastWriteTimeUtc)
                    return false;

                CopyFile(source.FullName, target);
                File.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
                return true;

            case ModFile.Write write:
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(write.Content))
                    return false;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, write.Content);
                return true;

            case ModFile.Delete:
                if (!File.Exists(target))
                    return false;

                File.Delete(target);
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(mod), mod, null);
        }
    }

    private void Restore(string rel, bool existed)
    {
        string target = Resolve(versionDirectory, rel);
        string backup = Resolve(backupDirectory, rel);

        if (!existed)
        {
            // A file the mod added: Roblox never had it.
            if (File.Exists(target))
                File.Delete(target);
            return;
        }

        if (!File.Exists(backup))
        {
            Logger.Warn(LogIdent, $"No backup of {rel}; a Roblox repair will bring it back");
            return;
        }

        CopyFile(backup, target);
        File.Delete(backup);
    }

    private static void CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    /// <summary>Joins a relative path to a root, refusing anything that would escape it.</summary>
    private static string Resolve(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd('\\') + '\\';
        string full = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Mod path '{relativePath}' points outside the Roblox folder");
        return full;
    }

    private static bool TryRun(string rel, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Usually a file Roblox has open right now; the next launch tries again.
            Logger.Warn(LogIdent, $"{rel}: {ex.Message}");
            return false;
        }
    }

    private Dictionary<string, bool> LoadApplied()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                var saved = JsonSerializer.Deserialize(File.ReadAllText(StateFile), NimbusJson.Default.DictionaryStringBoolean);
                if (saved is not null)
                    return new Dictionary<string, bool>(saved, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException ex)
        {
            Logger.Warn(LogIdent, $"Mod state unreadable, starting over: {ex.Message}");
        }

        return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveApplied(Dictionary<string, bool> applied)
    {
        Directory.CreateDirectory(backupDirectory);
        string temp = StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(applied, NimbusJson.Default.DictionaryStringBoolean));
        File.Move(temp, StateFile, overwrite: true);
    }
}
