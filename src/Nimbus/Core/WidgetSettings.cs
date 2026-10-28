namespace Nimbus.Core;

/// <summary>What the widget menu switches: the music card, and the stats shown in a line at its bottom.</summary>
public enum WidgetKind
{
    /// <summary>No longer offered (it can't be measured from outside Roblox); kept so older Widgets.json files still load.</summary>
    Ping,
    Server,
    Clock,
    Session,
    Cpu,
    Gpu,
    Music,
}

/// <summary>
/// Widgets on top of Roblox (Widgets.json). Kept out of Settings.json because the in-game menu changes it while the
/// settings window may be open in another process.
/// </summary>
public sealed class WidgetSettings
{
    public List<WidgetKind> Shown { get; set; } = [];

    /// <summary>The key that opens the widget menu in game (a Windows virtual-key code). Right Ctrl by default.</summary>
    public int MenuKey { get; set; } = 0xA3;

    /// <summary>Darken the game while the menu is open.</summary>
    public bool DimGame { get; set; } = true;

    /// <summary>White flakes falling behind the menu.</summary>
    public bool Snow { get; set; } = true;
}

internal static class WidgetStore
{
    private const string LogIdent = "Widgets";

    public static string FilePath => Path.Combine(Paths.Base, "Widgets.json");

    /// <summary>The saved widgets. Before this file existed, the music card had its own switch; that carries over.</summary>
    public static WidgetSettings Load(Settings settings)
    {
        if (!File.Exists(FilePath))
            return new WidgetSettings { Shown = settings.Integrations.MusicCard ? [WidgetKind.Music] : [] };

        var widgets = new JsonStore<WidgetSettings>(FilePath, NimbusJson.Default.WidgetSettings).Load();
        widgets.Shown.RemoveAll(k => k == WidgetKind.Ping);
        return widgets;
    }

    public static void Save(WidgetSettings widgets)
    {
        try
        {
            var store = new JsonStore<WidgetSettings>(FilePath, NimbusJson.Default.WidgetSettings);
            store.Load();
            CopyTo(widgets, store.Value);
            store.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn(LogIdent, $"Couldn't save widgets: {ex.Message}");
        }
    }

    private static void CopyTo(WidgetSettings from, WidgetSettings to)
    {
        to.Shown = [.. from.Shown];
        to.MenuKey = from.MenuKey;
        to.DimGame = from.DimGame;
        to.Snow = from.Snow;
    }

    /// <summary>"Right Ctrl", "F8", "Home"... for a virtual-key code.</summary>
    public static string KeyName(int vk)
    {
        string name = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk).ToString();
        return name switch
        {
            "RightCtrl" => Loc.T("Widgets.KeyRightCtrl"),
            "LeftCtrl" => Loc.T("Widgets.KeyLeftCtrl"),
            "RightShift" => Loc.T("Widgets.KeyRightShift"),
            "LeftShift" => Loc.T("Widgets.KeyLeftShift"),
            "RightAlt" => Loc.T("Widgets.KeyRightAlt"),
            "None" => $"#{vk}",
            _ => name,
        };
    }
}
