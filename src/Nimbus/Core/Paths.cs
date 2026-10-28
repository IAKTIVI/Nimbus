namespace Nimbus.Core;

/// <summary>Every file and folder Nimbus touches, all inside the user profile.</summary>
internal static class Paths
{
    /// <summary>%LocalAppData%\Nimbus, or NIMBUS_HOME when set (handy for development and tests).</summary>
    public static string Base { get; private set; } = ResolveBase();

    public static string Versions => Path.Combine(Base, "Versions");
    public static string Downloads => Path.Combine(Base, "Downloads");
    public static string Logs => Path.Combine(Base, "Logs");
    public static string SettingsFile => Path.Combine(Base, "Settings.json");
    public static string StateFile => Path.Combine(Base, "State.json");
    public static string InstalledExe => Path.Combine(Base, "Nimbus.exe");

    /// <summary>The user's own mods: files here replace the matching Roblox files.</summary>
    public static string Modifications => Path.Combine(Base, "Modifications");

    /// <summary>Files the user picked in the Mods page (cursor, font, death sound).</summary>
    public static string ModAssets => Path.Combine(Base, "Mods");

    /// <summary>Roblox's original files, saved before a mod replaces them. One folder per Roblox version.</summary>
    public static string ModBackups => Path.Combine(Base, "ModBackups");

    public static string CurrentExe => Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable");

    public static string StartMenuPrograms => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>True when running as a published single-file EXE rather than a dev build with loose DLLs.</summary>
    public static bool IsSingleFile => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Nimbus.dll"));

    public static bool IsRunningFromInstallDir =>
        string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), Path.GetFullPath(Base).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static void OverrideBase(string path) => Base = Path.GetFullPath(path);

    private static string ResolveBase()
    {
        string? overridden = Environment.GetEnvironmentVariable("NIMBUS_HOME");
        if (!string.IsNullOrWhiteSpace(overridden))
            return Path.GetFullPath(overridden);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nimbus");
    }
}
