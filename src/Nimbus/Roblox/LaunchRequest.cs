using System.Text.RegularExpressions;

namespace Nimbus.Roblox;

public enum LaunchKind
{
    /// <summary>No arguments: open the Nimbus window (or the installer on first run).</summary>
    None,

    /// <summary>-player: open the Roblox app itself.</summary>
    Player,

    /// <summary>A roblox-player: or roblox: link from the website or a shortcut.</summary>
    Link,

    /// <summary>-uninstall: from "Apps &amp; features".</summary>
    Uninstall,

    /// <summary>-settings: open the settings window directly, skipping the install check.</summary>
    Settings,

    /// <summary>-install: install (or update) Nimbus without the welcome window, then open it.</summary>
    Install,

    /// <summary>-update: auto-update's quiet install. No windows; the exit code says whether it worked.</summary>
    Update,

    /// <summary>-thanks: the thank-you card, shown once right after a fresh install.</summary>
    Thanks,
}

internal sealed partial record LaunchRequest(LaunchKind Kind, string? Link = null)
{
    public static LaunchRequest Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new LaunchRequest(LaunchKind.None);

        string first = args[0].Trim().Trim('"');

        if (first.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) || first.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase))
            return new LaunchRequest(LaunchKind.Link, StripChannel(first));

        return first.ToLowerInvariant() switch
        {
            "-player" => new LaunchRequest(LaunchKind.Player),
            "-uninstall" => new LaunchRequest(LaunchKind.Uninstall),
            "-settings" => new LaunchRequest(LaunchKind.Settings),
            "-install" => new LaunchRequest(LaunchKind.Install),
            "-update" => new LaunchRequest(LaunchKind.Update),
            "-thanks" => new LaunchRequest(LaunchKind.Thanks),
            _ => new LaunchRequest(LaunchKind.None),
        };
    }

    public bool LaunchesRoblox => Kind is LaunchKind.Player or LaunchKind.Link;

    /// <summary>What RobloxPlayerBeta.exe receives: the link itself, or --app to open the home screen.</summary>
    public string RobloxArgument => Kind == LaunchKind.Link ? Link! : "--app";

    /// <summary>The command line that starts this same launch again (used to hand over to an updated Nimbus).</summary>
    public string ToArguments() => Kind == LaunchKind.Link ? $"\"{Link}\"" : "-player";

    /// <summary>
    /// Website links carry a "+channel:xyz" part that would make Roblox try to switch release channels.
    /// Nimbus always runs the version it installed, so the channel is blanked.
    /// </summary>
    internal static string StripChannel(string link) => ChannelRegex().Replace(link, "+channel:");

    [GeneratedRegex(@"\+channel:[^+]*", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex();
}
