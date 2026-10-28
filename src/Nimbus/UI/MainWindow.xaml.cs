using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Nimbus.Core;
using Nimbus.Roblox;
using Nimbus.UI.Pages;

namespace Nimbus.UI;

public partial class MainWindow : Window
{
    private const string LogIdent = "MainWindow";

    private readonly Dictionary<string, UserControl> _pages = [];
    private readonly DispatcherTimer _popupCloser = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private DateTime _lastRobloxCheck = DateTime.MinValue;
    private bool _switchingPage;
    private bool _robloxUpdateWaiting;
    private string? _latestRoblox; // newest version hash from the last check, null if unknown or pinned

    public MainWindow()
    {
        InitializeComponent();
        Glass.Attach(this);

        VersionText.Text = AppInfo.Version;
        StateChanged += (_, _) => UpdateForWindowState();
        Closed += (_, _) => App.SaveSettings(); // flush anything still waiting on a debounce
        _popupCloser.Tick += (_, _) => ClosePopupIfLeft();
        PlayPopup.CustomPopupPlacementCallback = CenterUnderPlay;
        NavLeft.SelectedIndex = 0;
    }

    /// <summary>Switches to a page by its tag, e.g. "Profiles".</summary>
    public void Navigate(string key)
    {
        foreach (var list in new[] { NavLeft, NavRight })
        {
            if (list.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string)item.Tag == key) is { } item)
                list.SelectedItem = item;
        }
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingPage || sender is not ListBox { SelectedItem: ListBoxItem { Tag: string key } } list)
            return;

        // The icons are split in two lists around Play; only one may look selected.
        _switchingPage = true;
        (list == NavLeft ? NavRight : NavLeft).SelectedIndex = -1;
        _switchingPage = false;

        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "Launch" => new LaunchPage(),
                "Engine" => new EnginePage(),
                "Mods" => new ModsPage(),
                "Performance" => new PerformancePage(),
                "Integrations" => new IntegrationsPage(),
                "Widgets" => new WidgetsPage(),
                "History" => new HistoryPage(),
                "Profiles" => new ProfilesPage(),
                "Appearance" => new AppearancePage(),
                "About" => new AboutPage(),
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
            };
            _pages[key] = page;
        }
        else if (page is IPage shown)
        {
            shown.OnShown();
        }

        PageTitle.Text = Loc.T("Main." + key);
        PageHost.Content = page;
        PageScroller.ScrollToTop();
    }

    /// <summary>Once the page is scrolled, its top edge fades out so rows don't run hard into the page title.</summary>
    private void PageScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        PageScroller.OpacityMask = PageScroller.VerticalOffset > 0 ? TopFade : null;
    }

    private static readonly Brush TopFade = CreateTopFade();

    private static Brush CreateTopFade()
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 22),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1) },
        };
        brush.Freeze();
        return brush;
    }

    private void UpdateForWindowState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? Loc.T("Main.Restore") : Loc.T("Main.Maximize");

        // A maximized borderless window hangs a few pixels off-screen on every side.
        Root.Margin = maximized ? new Thickness(7) : new Thickness(0);
    }

    // ---------- Play ----------

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        PlayPopup.IsOpen = false;
        Process.Start(new ProcessStartInfo(Paths.CurrentExe, "-player") { UseShellExecute = false })?.Dispose();
    }

    private async void Play_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _popupCloser.Start();
        await RefreshRobloxStatusAsync();
        if (_robloxUpdateWaiting && PlayButton.IsMouseOver)
            PlayPopup.IsOpen = true;
    }

    /// <summary>Centers the panel a little below Play (sizes here are in device pixels).</summary>
    private CustomPopupPlacement[] CenterUnderPlay(Size popup, Size target, Point offset)
    {
        double gap = 10 * VisualTreeHelper.GetDpi(this).DpiScaleY;
        return [new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, target.Height + gap), PopupPrimaryAxis.Horizontal)];
    }

    private void Play_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => _popupCloser.Start();

    /// <summary>Closes the panel once the mouse is on neither Play nor the panel (there's a small gap between them).</summary>
    private void ClosePopupIfLeft()
    {
        if (PlayButton.IsMouseOver || PlayPanel.IsMouseOver)
            return;
        PlayPopup.IsOpen = false;
        _popupCloser.Stop();
    }

    /// <summary>Installed Roblox version and whether an update is waiting (it installs on the next launch anyway).</summary>
    private async Task RefreshRobloxStatusAsync()
    {
        var state = App.StateStore.Value;
        var deployment = RobloxDeployment.CreateDefault();
        string? installed = state.InstalledVersion is { } hash && RobloxDeployment.IsValidVersionHash(hash) && deployment.IsInstalled(hash) ? hash : null;
        _robloxUpdateWaiting = installed is null || (_latestRoblox is not null && !string.Equals(installed, _latestRoblox, StringComparison.OrdinalIgnoreCase));
        PlayVersionText.Text = installed is null
            ? "Roblox - " + Loc.T("LaunchPage.NotInstalled")
            : "Roblox " + (state.InstalledVersionNumber ?? installed);

        if (DateTime.UtcNow - _lastRobloxCheck < TimeSpan.FromMinutes(2))
            return;
        _lastRobloxCheck = DateTime.UtcNow;

        if (RobloxDeployment.IsValidVersionHash(App.Settings.PinnedVersion))
        {
            SetPlayStatus(Loc.T("LaunchPage.StayingPinned"), "TextTertiaryBrush");
            _latestRoblox = null;
            _robloxUpdateWaiting = installed is null;
            return;
        }

        SetPlayStatus(Loc.T("LaunchPage.Checking"), "TextTertiaryBrush");
        try
        {
            var latest = await deployment.GetLatestVersionAsync(CancellationToken.None);
            _latestRoblox = latest.ClientVersionUpload;
            _robloxUpdateWaiting = installed is null || !string.Equals(installed, latest.ClientVersionUpload, StringComparison.OrdinalIgnoreCase);
            if (installed is null)
                SetPlayStatus(Loc.T("Main.InstallsOnLaunch", latest.Version), "AccentBrush");
            else if (!_robloxUpdateWaiting)
                SetPlayStatus(Loc.T("LaunchPage.UpToDate"), "SuccessBrush");
            else
                SetPlayStatus(Loc.T("Main.UpdatesOnLaunch", latest.Version), "AccentBrush");
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Roblox update check failed: {ex.Message}");
            SetPlayStatus(Loc.T("LaunchPage.CheckFailed"), "DangerBrush");
            _lastRobloxCheck = DateTime.MinValue;
        }
    }

    private void SetPlayStatus(string text, string brush)
    {
        PlayStatusText.Text = text;
        PlayStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
