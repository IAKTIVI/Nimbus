using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nimbus.UI;

/// <summary>
/// A short message (server location, auto-rejoin...) shown at the top center of the Roblox window.
/// Windows notifications are held back while a fullscreen game runs ("Do not disturb while gaming"),
/// so Nimbus shows these itself. Like the music card: separate window, never takes focus,
/// only visible while Roblox is in front, and it counts down only while it's actually visible.
/// </summary>
public partial class GameToast : Window
{
    private const double MarginTop = 64; // below Roblox's top bar, 96-DPI pixels
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(1);

    private readonly RobloxWindowFinder _robloxWindow;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private nint _hwnd;
    private bool _shown;
    private TimeSpan _visibleLeft;
    private DateTime _postedAt;
    private DateTime _lastTick;

    internal GameToast(Process roblox)
    {
        _robloxWindow = new RobloxWindowFinder(roblox);
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            nint ex = GetWindowLongPtr(_hwnd, -20);
            SetWindowLongPtr(_hwnd, -20, ex | 0x08000000 | 0x80 | 0x8); // no-activate, tool window, topmost
            HwndSource.FromHwnd(_hwnd)?.AddHook((nint _, int msg, nint _, nint _, ref bool handled) =>
            {
                if (msg != 0x0021) // WM_MOUSEACTIVATE
                    return 0;
                handled = true;
                return 3; // MA_NOACTIVATE
            });
        };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Tests only: treat Roblox as the active window.</summary>
    internal bool AssumeRobloxActive { get; set; }

    internal bool IsOnScreen => _shown;

    internal (int X, int Y)? LastPosition { get; private set; }

    public void Start()
    {
        Left = -10000;
        Top = -10000;
        Show();
        ShowWindow(_hwnd, 0);
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Shows a message for <paramref name="visibleFor"/> of time that the player can actually see it.</summary>
    public void Post(string title, string text, string glyph, TimeSpan visibleFor)
    {
        TitleText.Text = title;
        BodyText.Text = text;
        IconText.Text = glyph;
        _visibleLeft = visibleFor;
        _postedAt = DateTime.UtcNow;
        _lastTick = DateTime.UtcNow;
        UpdateLayout();
        Tick();
    }

    private void Tick()
    {
        if (_hwnd == 0)
            return;

        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;

        nint roblox = _robloxWindow.Find();
        bool robloxActive = roblox != 0 && !IsIconic(roblox) && (AssumeRobloxActive || GetForegroundWindow() == roblox);
        bool pending = _visibleLeft > TimeSpan.Zero && now - _postedAt < GiveUpAfter;
        bool show = pending && robloxActive;

        if (show && GetClientRect(roblox, out var client))
        {
            var topLeft = new Point32 { X = client.Left, Y = client.Top };
            ClientToScreen(roblox, ref topLeft);
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int width = (int)Math.Round(ActualWidth * scale);
            int x = topLeft.X + (client.Right - client.Left - width) / 2;
            int y = topLeft.Y + (int)(MarginTop * scale);
            SetWindowPos(_hwnd, -1, x, y, 0, 0, 0x0001 | 0x0010 | 0x0040); // no size, no activate, show
            LastPosition = (x, y);
            _visibleLeft -= elapsed;
        }

        if (show != _shown)
        {
            ShowWindow(_hwnd, show ? 4 : 0); // SW_SHOWNOACTIVATE / SW_HIDE
            _shown = show;
        }
    }

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
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
