using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.UI;

/// <summary>
/// The music card that sits in the top right of the Roblox window, for whatever plays on the PC (Spotify app,
/// YouTube or any site in a browser, other players). It's a separate window on top of Roblox
/// (nothing is injected into Roblox), built so it never interrupts the game:
/// clicks don't take focus away from Roblox, and it's only shown while Roblox is the active window.
/// It takes no keys: it's switched on and off from the widget menu (see <see cref="WidgetHost"/>), which also puts
/// the stats line (CPU, GPU, time...) at its bottom. With the music part off or nothing playing, only that line shows.
/// </summary>
public partial class MusicOverlay : Window
{
    private const string LogIdent = "MusicCard";

    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);

    /// <summary>Tests only: treat Roblox as the active window so the card can be checked while another window is in front.</summary>
    internal bool AssumeRobloxActive { get; set; }

    /// <summary>Where the card was last put: its top-left, and the top-right corner of Roblox's game area (screen pixels).</summary>
    internal (int X, int Y, int RobloxRight, int RobloxTop)? LastPlacement { get; private set; }

    internal bool IsOnScreen => _shown;

    internal nint Handle => _hwnd;

    /// <summary>The music part is switched off (widget menu or Integrations page); the stats line can still show.</summary>
    internal bool UserHidden
    {
        get => _userHidden;
        set
        {
            if (_userHidden == value)
                return;
            _userHidden = value;
            Place();
        }
    }

    private bool _userHidden;

    /// <summary>While the widget menu is open the card shows even with nothing playing, so it can be moved.</summary>
    internal bool ShowWhenIdle
    {
        get => _showWhenIdle;
        set
        {
            if (_showWhenIdle == value)
                return;
            _showWhenIdle = value;
            Place();
        }
    }

    private bool _showWhenIdle;
    private bool _hasStats;

    /// <summary>The stats line at the bottom ("CPU 19% - GPU 12% - 20:22"), or null for none.</summary>
    internal void SetStats(string? stats)
    {
        bool has = !string.IsNullOrEmpty(stats);
        if (has && StatsText.Text != stats)
            StatsText.Text = stats;
        if (has == _hasStats)
            return;
        _hasStats = has;
        Place();
    }

    internal MediaState? Current => _now;

    private readonly RobloxWindowFinder _robloxWindow;
    private readonly DispatcherTimer _placeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint _hwnd;
    private bool _shown;
    private bool _polling;
    private DateTime _lastPoll = DateTime.MinValue;
    private MediaState? _now;
    private bool _dragging;

    /// <summary>The card started (true) or stopped (false) being dragged.</summary>
    internal event Action<bool>? DragChanged;
    private Point32 _dragFrom;
    private (int X, int Y) _windowFrom;
    private bool _seeking;
    private double _seekFraction;
    private byte[]? _cover;

    internal MusicOverlay(Process roblox)
    {
        _robloxWindow = new RobloxWindowFinder(roblox);
        InitializeComponent();
        SourceInitialized += (_, _) => Prepare();
        _placeTimer.Tick += (_, _) => Place();
        _tickTimer.Tick += (_, _) => UpdateProgress();
    }

    public void Start()
    {
        // Shown once off-screen (without taking focus) so WPF renders it, then hidden:
        // from here on Place() decides when it's visible, through Win32 so it never activates.
        Left = -10000;
        Top = -10000;
        Show();
        ShowWindow(_hwnd, SwHide);
        _placeTimer.Start();
        _tickTimer.Start();
        Closed += (_, _) =>
        {
            _placeTimer.Stop();
            _tickTimer.Stop();
        };
    }

    private void Prepare()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        nint ex = GetWindowLongPtr(_hwnd, GwlExStyle);
        SetWindowLongPtr(_hwnd, GwlExStyle, ex | (nint)(WsExNoActivate | WsExToolWindow | WsExTopmost));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmMouseActivate:
                // Clicking the card must never take keyboard focus from Roblox.
                handled = true;
                return MaNoActivate;
        }

        return 0;
    }

    /// <summary>Keeps the card glued to the top right of Roblox, and only visible while Roblox is in front.</summary>
    private void Place()
    {
        if (_hwnd == 0)
            return;

        nint roblox = _robloxWindow.Find();
        nint foreground = GetForegroundWindow();
        bool robloxActive = roblox != 0 && (AssumeRobloxActive || foreground == roblox || foreground == _hwnd || WidgetHost.IsOwnWindow(foreground)) && !IsIconic(roblox);

        if (robloxActive && !_userHidden && (DateTime.UtcNow - _lastPoll > PollEvery))
            _ = PollAsync();

        bool musicShown = !_userHidden && (_now is not null || _showWhenIdle);
        if (musicShown && _now is null)
            ShowIdle();
        MusicPart.Visibility = musicShown ? Visibility.Visible : Visibility.Collapsed;
        StatsPart.Visibility = _hasStats ? Visibility.Visible : Visibility.Collapsed;
        StatsPart.BorderThickness = new Thickness(0, musicShown ? 1 : 0, 0, 0);
        StatsPart.Margin = new Thickness(0, musicShown ? 6 : 0, 0, 0);
        StatsPart.Padding = musicShown ? new Thickness(0, 6, 0, 1) : new Thickness(0, 0, 0, 4); // stats only: centred in the card
        bool show = robloxActive && (musicShown || _hasStats);
        if (show && !_dragging && GameArea(roblox) is { } area)
        {
            var (cardWidth, cardHeight) = CardSize();
            var (x, y) = CardPlacement.Position(CardPlacement.Load(), area.Left, area.Top, area.Width, area.Height, cardWidth, cardHeight,
                VisualTreeHelper.GetDpi(this).DpiScaleX);
            SetWindowPos(_hwnd, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate | SwpShowWindow);
            LastPlacement = (x, y, area.Left + area.Width, area.Top);
        }

        if (show != _shown)
        {
            ShowWindow(_hwnd, show ? SwShowNoActivate : SwHide);
            _shown = show;
        }
    }

    private async Task PollAsync()
    {
        if (_polling)
            return;

        _polling = true;
        _lastPoll = DateTime.UtcNow;
        try
        {
            Display(await PcMedia.GetAsync());
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>The empty card: "Nothing playing", buttons faded.</summary>
    private void ShowIdle()
    {
        TitleText.Text = Loc.T("Music.NothingPlaying");
        ArtistText.Text = "";
        Cover.Source = null;
        _cover = null;
        ProgressPanel.Visibility = Visibility.Collapsed;
        foreach (var button in new[] { ShuffleButton, PreviousButton, PlayButton, NextButton, RepeatButton })
            button.IsEnabled = false;
    }

    internal void Display(MediaState? now)
    {
        _now = now;
        if (now is null)
            return;

        PlayButton.IsEnabled = true;

        TitleText.Text = now.Title;
        ArtistText.Text = now.Artist;
        PlayButton.Content = now.IsPlaying ? "" : "";
        PlayButton.ToolTip = now.IsPlaying ? Loc.T("Music.Pause") : Loc.T("Music.Play");
        NextButton.IsEnabled = now.CanNext;
        PreviousButton.IsEnabled = now.CanPrevious;

        // Apps like YouTube in a browser don't support shuffle/repeat: those buttons fade out.
        ShuffleButton.IsEnabled = now.CanShuffle && now.Shuffle is not null;
        ShuffleButton.Foreground = now.Shuffle == true ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("CardDim");
        RepeatButton.IsEnabled = now.CanRepeat && now.Repeat is not null;
        RepeatButton.Content = now.Repeat == RepeatMode.Track ? "" : "";
        RepeatButton.Foreground = now.Repeat is null or RepeatMode.None ? (Brush)FindResource("CardDim") : (Brush)FindResource("AccentBrush");

        if (!ReferenceEquals(now.Cover, _cover))
        {
            _cover = now.Cover;
            Cover.Source = ToImage(now.Cover);
        }

        UpdateProgress();
    }

    private static BitmapImage? ToImage(byte[]? bytes)
    {
        if (bytes is null)
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 88;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Moves the bar smoothly between polls.</summary>
    private void UpdateProgress()
    {
        bool hasTimeline = _now is { Duration.Ticks: > 0 };
        ProgressPanel.Visibility = hasTimeline ? Visibility.Visible : Visibility.Collapsed;
        if (!hasTimeline)
            return;

        if (_seeking)
            return;

        var now = _now!;
        var position = now.PositionNow(DateTimeOffset.UtcNow);
        ProgressFill.Width = (SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : 274) * (position / now.Duration);
        SeekArea.Cursor = now.CanSeek ? System.Windows.Input.Cursors.Hand : null;
        ElapsedText.Text = Format(position);
        RemainingText.Text = "-" + Format(now.Duration - position);
    }

    internal static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---------- Buttons (the card updates right away, the app catches up) ----------

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_now is not { } now)
            return;

        var at = DateTimeOffset.UtcNow;
        Display(now with { IsPlaying = !now.IsPlaying, Position = now.PositionNow(at), PositionAt = at });
        await PcMedia.TogglePlayPauseAsync();
        await RefreshSoonAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        await PcMedia.NextAsync();
        await RefreshSoonAsync();
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        await PcMedia.PreviousAsync();
        await RefreshSoonAsync();
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        if (_now is not { Shuffle: { } shuffle } now)
            return;

        Display(now with { Shuffle = !shuffle });
        await PcMedia.SetShuffleAsync(!shuffle);
        await RefreshSoonAsync();
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if (_now is not { Repeat: { } repeat } now)
            return;

        var next = PcMedia.NextRepeat(repeat);
        Display(now with { Repeat = next });
        await PcMedia.SetRepeatAsync(next);
        await RefreshSoonAsync();
    }

    private async Task RefreshSoonAsync()
    {
        await Task.Delay(300);
        _lastPoll = DateTime.MinValue; // the next placement tick reads the new state right away
    }
    // ---------- Moving the card ----------

    /// <summary>Roblox's game area (client rect) in screen pixels, or null if it can't be read.</summary>
    private static (int Left, int Top, int Width, int Height)? GameArea(nint roblox)
    {
        if (!GetClientRect(roblox, out var client))
            return null;
        var topLeft = new Point32 { X = client.Left, Y = client.Top };
        ClientToScreen(roblox, ref topLeft);
        return (topLeft.X, topLeft.Y, client.Right - client.Left, client.Bottom - client.Top);
    }

    private (int Width, int Height) CardSize()
    {
        GetWindowRect(_hwnd, out var r);
        if (r.Right - r.Left > 0)
            return (r.Right - r.Left, r.Bottom - r.Top);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return ((int)Math.Round(ActualWidth * scale), (int)Math.Round(ActualHeight * scale));
    }

    private void Card_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            CardPlacement.Reset(); // double-click: back to the top right
            Place();
            e.Handled = true;
            return;
        }

        GetCursorPos(out _dragFrom);
        GetWindowRect(_hwnd, out var r);
        _windowFrom = (r.Left, r.Top);
        _dragging = true;
        DragChanged?.Invoke(true);
        Card.CaptureMouse();
        e.Handled = true;
    }

    private void Card_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
            return;

        GetCursorPos(out var now);
        SetWindowPos(_hwnd, HwndTopmost, _windowFrom.X + now.X - _dragFrom.X, _windowFrom.Y + now.Y - _dragFrom.Y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    private void Card_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        DragChanged?.Invoke(false);
        Card.ReleaseMouseCapture();
        if (_robloxWindow.Find() is var robloxWindow and not 0 && GameArea(robloxWindow) is { } area)
        {
            GetWindowRect(_hwnd, out var r);
            var (cardWidth, cardHeight) = CardSize();
            CardPlacement.Save(CardPlacement.ToFractions(r.Left, r.Top, area.Left, area.Top, area.Width, area.Height, cardWidth, cardHeight));
        }

        Place(); // snaps back inside the Roblox window if it was dropped outside
        e.Handled = true;
    }

    // ---------- Jumping in the song ----------

    private bool CanSeek => _now is { CanSeek: true, Duration.Ticks: > 0 };

    private void Seek_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (CanSeek)
            Bar.Height = 5;
    }

    private void Seek_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_seeking)
            Bar.Height = 3;
    }

    private void Seek_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true; // never starts dragging the card
        if (!CanSeek)
            return;

        _seeking = true;
        SeekArea.CaptureMouse();
        PreviewSeek(e.GetPosition(SeekArea).X);
    }

    private void Seek_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_seeking)
            PreviewSeek(e.GetPosition(SeekArea).X);
    }

    private async void Seek_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_seeking || _now is not { } now)
            return;

        _seeking = false;
        SeekArea.ReleaseMouseCapture();
        if (!SeekArea.IsMouseOver)
            Bar.Height = 3;

        var target = now.Duration * _seekFraction;
        Display(now with { Position = target, PositionAt = DateTimeOffset.UtcNow });
        await PcMedia.SeekAsync(target);
        await RefreshSoonAsync();
    }

    /// <summary>Moves the bar and times under the mouse while dragging; the jump happens on release.</summary>
    private void PreviewSeek(double x)
    {
        if (_now is not { } now)
            return;

        _seekFraction = SeekFraction(x, SeekArea.ActualWidth);
        var position = now.Duration * _seekFraction;
        ProgressFill.Width = SeekArea.ActualWidth * _seekFraction;
        ElapsedText.Text = Format(position);
        RemainingText.Text = "-" + Format(now.Duration - position);
    }

    internal static double SeekFraction(double x, double width) => width <= 0 ? 0 : Math.Clamp(x / width, 0, 1);

    // ---------- Win32 ----------

    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExTopmost = 0x00000008;
    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

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
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point32 point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
