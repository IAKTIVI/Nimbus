using System.IO.Compression;

namespace Nimbus.Roblox;

internal static class ZipExtractor
{
    /// <summary>
    /// Extracts a Roblox package. Entries use backslash paths and start with a "\" root entry;
    /// any entry that would land outside <paramref name="destination"/> is rejected.
    /// </summary>
    public static void Extract(string zipPath, string destination)
    {
        string root = Path.GetFullPath(destination);
        if (!root.EndsWith('\\'))
            root += '\\';

        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            string relative = entry.FullName.Replace('/', '\\').TrimStart('\\');
            if (relative.Length == 0)
                continue;

            string target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Blocked unsafe path '{entry.FullName}' in {Path.GetFileName(zipPath)}");

            if (relative.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
