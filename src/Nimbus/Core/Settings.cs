namespace Nimbus.Core;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum LaunchStyle
{
    /// <summary>The glass card with icon, status and progress bar.</summary>
    GlassCard,

    /// <summary>A small bar with just the status and progress.</summary>
    Minimal,

    /// <summary>Nothing on normal launches; the card only appears while Roblox updates (or on errors).</summary>
    HiddenUnlessUpdating,
}

/// <summary>User preferences, saved to Settings.json.</summary>
public sealed class Settings
{
    // Launch
    public bool MultiInstance { get; set; }

    /// <summary>A "Roblox (Nimbus)" icon on the desktop. Created on install / update while this is on.</summary>
    public bool DesktopShortcut { get; set; } = true;

    /// <summary>A Roblox version hash such as "version-5a21b87bbc2641cf"; null means always use the latest.</summary>
    public string? PinnedVersion { get; set; }

    // Engine (FastFlags)
    public EngineSettings Engine { get; set; } = new();

    /// <summary>Raw JSON from the advanced FastFlag editor. Applied on top of the toggles.</summary>
    public string CustomFlags { get; set; } = "";

    /// <summary>The user's own saved profiles (the built-in ones live in Profiles).</summary>
    public List<Profile> Profiles { get; set; } = [];

    // Mods (files changed inside the Roblox install)
    public ModSettings Mods { get; set; } = new();

    // Performance
    public PerformanceSettings Performance { get; set; } = new();

    // While Roblox runs
    public IntegrationSettings Integrations { get; set; } = new();

    /// <summary>UI language code ("en", "de"...). Set on first start: Windows' language for new installs, English for updates.</summary>
    public string? Language { get; set; }

    // Appearance (dark by default: the Lumen-style glass look)
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string AccentColor { get; set; } = "#0A84FF";

    public LaunchStyle LaunchStyle { get; set; }

    // About
    /// <summary>Installs new Nimbus versions from GitHub by itself, right before Roblox starts.</summary>
    public bool AutoUpdate { get; set; } = true;
}

/// <summary>Things Nimbus remembers about the machine, saved to State.json. Not user-editable.</summary>
public sealed class AppState
{
    /// <summary>The Roblox version hash currently installed under Versions\.</summary>
    public string? InstalledVersion { get; set; }

    /// <summary>Human-readable version number, e.g. "0.745.0.7451137".</summary>
    public string? InstalledVersionNumber { get; set; }

    /// <summary>The roblox / roblox-player handlers that were registered before Nimbus, restored on uninstall.</summary>
    public Dictionary<string, string> PreviousProtocolHandlers { get; set; } = [];

    /// <summary>Roblox EXE paths Nimbus set Windows GPU / fullscreen options for, so they can be removed again.</summary>
    public List<string> TweakedExePaths { get; set; } = [];

    /// <summary>When auto-rejoin last kicked in, so it can give up after a few tries.</summary>
    public List<DateTimeOffset> RecentRejoins { get; set; } = [];
}
