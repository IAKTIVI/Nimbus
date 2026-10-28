using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.UI;

/// <summary>
/// The widgets for one Roblox window: the stats line at the bottom of the music card (CPU, GPU, time, session,
/// server) and the in-game widget menu that switches them on and off. Nothing is injected into or read from the game.
/// </summary>
internal sealed class WidgetHost
{
    private static readonly HashSet<nint> OwnWindows = [];

    /// <summary>The order the stats line uses.</summary>
    private static readonly WidgetKind[] StatsOrder = [WidgetKind.Cpu, WidgetKind.Gpu, WidgetKind.Clock, WidgetKind.Session, WidgetKind.Server];

    private readonly RobloxWindowFinder _robloxWindow;
    private readonly Settings _settings;
    private readonly MusicOverlay? _music;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _valueTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _menuTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly SystemLoad _load = new();
    private WidgetSettings _widgets;
    private DateTime _widgetsWritten;
    private KeyWatcher? _keys;
    private WidgetMenu? _menu;
    private (int, int, int, int)? _menuArea;
    private DateTimeOffset? _joined;
    private string? _location;

    public WidgetHost(Process roblox, Settings settings, MusicOverlay? music)
    {
        _robloxWindow = new RobloxWindowFinder(roblox);
        _settings = settings;
        _music = music;
        if (music is not null)
            music.DragChanged += dragging => _menu?.PauseSnow(dragging); // the snow redraws the whole screen; dragging stays smooth without it
        _widgets = WidgetStore.Load(settings);
        _widgetsWritten = WrittenAt();
    }

    /// <summary>Tests only: act as if Roblox is in front (and never take focus), so it can be checked off screen.</summary>
    internal bool AssumeRobloxActive { get; set; }

    /// <summary>Tests only: a stand-in window for Roblox's.</summary>
    internal Func<nint>? WindowOverride { get; set; }

    internal WidgetMenu? Menu => _menu;

    /// <summary>True for the widget menu: having it in front still counts as "in Roblox".</summary>
    public static bool IsOwnWindow(nint hwnd)
    {
        lock (OwnWindows)
            return OwnWindows.Contains(hwnd);
    }

    /// <summary>What the widget menu and the Integrations page list (no ping: it can't be measured from outside Roblox).</summary>
    public static IEnumerable<WidgetKind> Choices => [WidgetKind.Music, .. StatsOrder];

    /// <summary>Whether the server's location is needed (only the server widget shows it).</summary>
    public bool WantsLocation => _widgets.Shown.Contains(WidgetKind.Server);

    public void Start()
    {
        ApplyMusic();
        _valueTimer.Tick += (_, _) => RefreshValues();
        _menuTimer.Tick += (_, _) => FollowRoblox();
        _valueTimer.Start();
        RefreshValues();

        _keys = new KeyWatcher(_widgets.MenuKey);
        _keys.Pressed += () => _dispatcher.BeginInvoke(OnMenuKey);
    }

    public void Close()
    {
        _valueTimer.Stop();
        _keys?.Dispose();
        CloseMenu();
        _load.Dispose();
    }

    // ---------- What the activity watcher tells us ----------

    public void OnJoined(DateTimeOffset started)
    {
        _joined = started;
        _location = null;
    }

    public void OnLocation(string location) => _location = location;

    public void OnLeft()
    {
        _joined = null;
        _location = null;
    }

    // ---------- The stats line ----------

    private void RefreshValues()
    {
        // The Integrations page may have changed something while Roblox runs.
        if (_menu is null && WrittenAt() != _widgetsWritten)
        {
            _widgets = WidgetStore.Load(_settings);
            _widgetsWritten = WrittenAt();
            if (_keys is not null)
                _keys.Key = _widgets.MenuKey;
            ApplyMusic();
        }

        _music?.SetStats(StatsLine(_widgets.Shown, Value));
    }

    /// <summary>"CPU 19%  -  GPU 12%  -  20:22" for the widgets that are on and have a value; null if none.</summary>
    internal static string? StatsLine(IReadOnlyCollection<WidgetKind> shown, Func<WidgetKind, string?> value)
    {
        var parts = StatsOrder.Where(shown.Contains).Select(value).Where(v => !string.IsNullOrEmpty(v)).ToList();
        return parts.Count == 0 ? null : string.Join("  -  ", parts);
    }

    private string? Value(WidgetKind kind) => kind switch
    {
        WidgetKind.Cpu => _load.Cpu() is { } cpu ? $"CPU {cpu}%" : null,
        WidgetKind.Gpu => _load.Gpu() is { } gpu ? $"GPU {gpu}%" : null,
        WidgetKind.Clock => DateTime.Now.ToString("t", CultureInfo.CurrentUICulture),
        WidgetKind.Session => _joined is { } joined ? $"{Loc.T("Widgets.LabelSession")} {SessionTime(DateTimeOffset.UtcNow - joined)}" : null,
        WidgetKind.Server => _location,
        _ => null,
    };

    internal static string SessionTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    private void ApplyMusic()
    {
        if (_music is not null)
            _music.UserHidden = !_widgets.Shown.Contains(WidgetKind.Music);
    }

    // ---------- Menu ----------

    private nint RobloxWindow()
    {
        if (WindowOverride is { } window)
            return window();
        return _robloxWindow.Find();
    }

    internal void OnMenuKey()
    {
        if (_menu is not null)
        {
            CloseMenu();
            return;
        }

        nint roblox = RobloxWindow();
        if (roblox == 0 || (!AssumeRobloxActive && GetForegroundWindow() != roblox) || IsIconic(roblox) || GameArea(roblox) is not { } area)
            return;

        var menu = new WidgetMenu { Left = -10000, Top = -10000, Width = 10, Height = 10 };
        menu.Fill(_widgets);
        menu.Toggled += Menu_Toggled;
        menu.ResetRequested += CardPlacement.Reset;
        menu.CloseRequested += CloseMenu;
        menu.Show();
        _menu = menu;
        lock (OwnWindows)
            OwnWindows.Add(menu.Handle);
        SetWindowPos(menu.Handle, HwndTopmost, area.Left, area.Top, area.Width, area.Height, SwpNoActivate);
        _menuArea = area;

        // The card shows (even with nothing playing) so it can be moved; its own timer keeps it above the menu.
        if (_music is not null)
            _music.ShowWhenIdle = true;
        _menuTimer.Start();
    }

    /// <summary>Keeps the menu over Roblox while it's open, and closes it if you Alt+Tab away.</summary>
    private void FollowRoblox()
    {
        if (_menu is null)
        {
            _menuTimer.Stop();
            return;
        }

        nint roblox = RobloxWindow();
        nint foreground = GetForegroundWindow();
        bool inRoblox = roblox != 0 && (AssumeRobloxActive || foreground == roblox || IsOwnWindow(foreground) || IsMusicCard(foreground)) && !IsIconic(roblox);
        if (!inRoblox)
            CloseMenu();
        else if (GameArea(roblox) is { } area && area != _menuArea)
        {
            // Only when Roblox moved, and without touching the stacking order: the music card stays above the menu
            // (re-raising the menu every tick made the two swap places and flicker).
            SetWindowPos(_menu.Handle, 0, area.Left, area.Top, area.Width, area.Height, SwpNoActivate | SwpNoZOrder);
            _menuArea = area;
        }
    }

    private void Menu_Toggled(WidgetKind kind, bool on)
    {
        _widgets.Shown.Remove(kind);
        if (on)
            _widgets.Shown.Add(kind);
        WidgetStore.Save(_widgets);
        _widgetsWritten = WrittenAt();
        ApplyMusic();
        RefreshValues();
    }

    /// <summary>Roblox never lost the keyboard (the menu doesn't take it), so there's nothing to hand back.</summary>
    private void CloseMenu()
    {
        if (_menu is not { } menu)
            return;

        _menu = null;
        _menuTimer.Stop();
        lock (OwnWindows)
            OwnWindows.Remove(menu.Handle);
        menu.Close();
        if (_music is not null)
            _music.ShowWhenIdle = false;
    }

    private bool IsMusicCard(nint hwnd) => _music is not null && hwnd != 0 && hwnd == _music.Handle;

    private static DateTime WrittenAt()
    {
        try
        {
            return File.GetLastWriteTimeUtc(WidgetStore.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Roblox's game area (client rect) in screen pixels.</summary>
    private static (int Left, int Top, int Width, int Height)? GameArea(nint roblox)
    {
        if (roblox == 0 || !GetClientRect(roblox, out var client))
            return null;
        var topLeft = new Point32 { X = client.Left, Y = client.Top };
        ClientToScreen(roblox, ref topLeft);
        int width = client.Right - client.Left, height = client.Bottom - client.Top;
        return width <= 0 || height <= 0 ? null : (topLeft.X, topLeft.Y, width, height);
    }

    // ---------- Win32 ----------

    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point32 point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
