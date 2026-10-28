using Microsoft.Win32;
using Nimbus.Core;

namespace Nimbus.Installation;

/// <summary>Makes roblox-player:// and roblox:// links (the website's Play button) open Nimbus.</summary>
internal static class ProtocolHandler
{
    private const string LogIdent = "Protocol";
    private static readonly string[] Protocols = ["roblox-player", "roblox"];

    public static string CommandFor(string exe) => $"\"{exe}\" %1";

    public static bool IsRegistered(string exe) =>
        Protocols.All(p => string.Equals(GetCommand(p), CommandFor(exe), StringComparison.OrdinalIgnoreCase));

    public static void Register(string exe, AppState state)
    {
        foreach (string protocol in Protocols)
        {
            string? existing = GetCommand(protocol);
            if (string.Equals(existing, CommandFor(exe), StringComparison.OrdinalIgnoreCase))
                continue;

            // Remember who handled links before (Roblox itself, or another launcher) so uninstall can hand them back.
            if (!string.IsNullOrEmpty(existing) && !existing.Contains("Nimbus.exe", StringComparison.OrdinalIgnoreCase))
                state.PreviousProtocolHandlers[protocol] = existing;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}");
            key.SetValue("", "URL: Roblox Protocol");
            key.SetValue("URL Protocol", "");

            using (var icon = key.CreateSubKey("DefaultIcon"))
                icon.SetValue("", $"\"{exe}\",0");

            using (var command = key.CreateSubKey(@"shell\open\command"))
                command.SetValue("", CommandFor(exe));

            Logger.Info(LogIdent, $"{protocol}:// now opens Nimbus (was: {existing ?? "nothing"})");
        }
    }

    /// <summary>Gives links back to the previous handler, or to the newest official Roblox install if that one is gone.</summary>
    public static void Unregister(AppState state)
    {
        string? officialRoblox = FindOfficialRobloxPlayer();

        foreach (string protocol in Protocols)
        {
            string? current = GetCommand(protocol);
            if (current is null || !current.Contains("Nimbus.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            string? restore = state.PreviousProtocolHandlers.GetValueOrDefault(protocol);
            if (restore is not null && !File.Exists(ExtractExe(restore)))
                restore = null;

            restore ??= officialRoblox is null ? null : $"\"{officialRoblox}\" %1";

            if (restore is null)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{protocol}", throwOnMissingSubKey: false);
                Logger.Info(LogIdent, $"{protocol}:// unregistered");
                continue;
            }

            string exe = ExtractExe(restore);
            using (var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}\DefaultIcon"))
                icon.SetValue("", $"\"{exe}\",0");
            using (var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}\shell\open\command"))
                command.SetValue("", restore);

            Logger.Info(LogIdent, $"{protocol}:// handed back to {restore}");
        }
    }

    public static string? GetCommand(string protocol)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{protocol}\shell\open\command");
        return key?.GetValue("") as string;
    }

    /// <summary>The newest RobloxPlayerBeta.exe installed by Roblox's own installer, if any.</summary>
    private static string? FindOfficialRobloxPlayer()
    {
        string versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
        if (!Directory.Exists(versions))
            return null;

        return new DirectoryInfo(versions)
            .EnumerateDirectories()
            .Select(d => Path.Combine(d.FullName, "RobloxPlayerBeta.exe"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>"C:\x\Roblox.exe" %1  =>  C:\x\Roblox.exe</summary>
    private static string ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }

        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
