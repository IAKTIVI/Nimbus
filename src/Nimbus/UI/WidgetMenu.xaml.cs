using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Nimbus.Core;

namespace Nimbus.UI;

/// <summary>
/// The in-game widget menu (Right Ctrl by default): switch widgets on and off, drag them, done.
/// It's used with the mouse only and never becomes the active window, so Roblox keeps the keyboard the whole time
/// (taking it away left keys like Shift stuck in Roblox, so you couldn't run afterwards).
/// </summary>
public partial class WidgetMenu : Window
{
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)));
    private static readonly Brush Clear = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0))); // still catches clicks

    private readonly List<Flake> _flakes = [];
    private readonly Random _random = new();
    private TimeSpan _lastFrame;
    private bool _wantSnow;

    /// <summary>A widget was switched on or off.</summary>
    internal event Action<WidgetKind, bool>? Toggled;

    internal event Action? ResetRequested;

    internal event Action? CloseRequested;

    internal WidgetMenu()
    {
        InitializeComponent();
        // It opens off screen and is then stretched over Roblox; the snow starts once it has that size.
        SizeChanged += (_, _) =>
        {
            if (_wantSnow && ActualWidth > 200)
                StartSnow();
        };
        SourceInitialized += (_, _) =>
        {
            nint hwnd = Handle;
            SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            HwndSource.FromHwnd(hwnd)?.AddHook(NoActivate);
        };
        GlowStop.Color = Color.FromArgb(0x47, Accent.R, Accent.G, Accent.B);
    }

    private static Color Accent => Application.Current?.TryFindResource("AccentColor") is Color color ? color : Color.FromRgb(0x8B, 0x5C, 0xF6);

    /// <summary>Clicks never activate the menu (see the class summary).</summary>
    private static nint NoActivate(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }
        return 0;
    }

    internal void Fill(WidgetSettings widgets)
    {
        Root.Background = widgets.DimGame ? Dim : Clear;

        Toggles.Children.Clear();
        foreach (var kind in WidgetHost.Choices)
        {
            if (Toggles.Children.Count > 0)
                Toggles.Children.Add(new Border { Style = (Style)FindResource("MenuSeparator") });

            var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = widgets.Shown.Contains(kind), Focusable = false, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(toggle, Loc.T("Widgets." + kind));
            toggle.Click += (_, _) => Toggled?.Invoke(kind, toggle.IsChecked == true);

            // The whole row switches it, like the settings rows
            var row = new DockPanel { Margin = new Thickness(18, 10, 16, 10), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            DockPanel.SetDock(toggle, Dock.Right);
            row.Children.Add(toggle);
            row.Children.Add(new TextBlock { Text = Loc.T("Widgets." + kind), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsInside(source, toggle))
                    return;
                toggle.IsChecked = toggle.IsChecked != true;
                Toggled?.Invoke(kind, toggle.IsChecked == true);
            };
            Toggles.Children.Add(row);
        }

        _wantSnow = widgets.Snow;
    }

    internal nint Handle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();

    private void Done_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseRequested?.Invoke();

    /// <summary>Clicks on the list itself don't count as "outside".</summary>
    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current == container)
                return true;
        }
        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSnow();
        base.OnClosed(e);
    }

    // ---------- Snow ----------

    private sealed record Flake(Ellipse Shape, double Speed, double Drift, double Phase)
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    private void StartSnow()
    {
        if (_flakes.Count > 0 || ActualWidth <= 0)
            return;

        // About one flake per 18 000 square pixels (≈ 110 on a 1080p window), capped so it stays light.
        int count = (int)Math.Clamp(ActualWidth * ActualHeight / 18000, 30, 140);
        for (int i = 0; i < count; i++)
        {
            double size = 1.5 + _random.NextDouble() * 3;
            var shape = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.35 + _random.NextDouble() * 0.55 };
            var flake = new Flake(shape, 18 + size * 12 + _random.NextDouble() * 20, 8 + _random.NextDouble() * 18, _random.NextDouble() * Math.PI * 2)
            {
                X = _random.NextDouble() * ActualWidth,
                Y = _random.NextDouble() * ActualHeight,
            };
            _flakes.Add(flake);
            Snow.Children.Add(shape);
            Canvas.SetLeft(shape, flake.X);
            Canvas.SetTop(shape, flake.Y);
        }

        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += Animate;
    }

    /// <summary>Freezes the flakes where they are (while the music card is dragged), and lets them fall again.</summary>
    internal void PauseSnow(bool pause)
    {
        if (_flakes.Count == 0)
            return;
        CompositionTarget.Rendering -= Animate;
        if (!pause)
        {
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += Animate;
        }
    }

    private void StopSnow()
    {
        CompositionTarget.Rendering -= Animate;
        _flakes.Clear();
        Snow.Children.Clear();
    }

    private void Animate(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double seconds = _lastFrame == TimeSpan.Zero ? 0 : Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;
        if (seconds <= 0)
            return;

        double t = now.TotalSeconds;
        foreach (var flake in _flakes)
        {
            flake.Y += flake.Speed * seconds;
            if (flake.Y > ActualHeight)
            {
                flake.Y = -6;
                flake.X = _random.NextDouble() * ActualWidth;
            }

            Canvas.SetLeft(flake.Shape, flake.X + Math.Sin(t * 0.8 + flake.Phase) * flake.Drift);
            Canvas.SetTop(flake.Shape, flake.Y);
        }
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ---------- Win32 ----------

    private const int GwlExStyle = -20;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x80;
    private const int WmMouseActivate = 0x0021;
    private const nint MaNoActivate = 3;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
