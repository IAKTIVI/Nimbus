using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nimbus.Core;
using Nimbus.UI;

namespace Nimbus.Tests;

public class WidgetDemoTests
{
    /// <summary>
    /// NIMBUS_WIDGET_PNG=&lt;folder&gt;: the music card with the stats line, the stats-only card, and the open widget
    /// menu over a stand-in game, saved as pictures (rendered off screen; nothing appears on the desktop).
    /// </summary>
    [Fact]
    public void RenderWidgets()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_WIDGET_PNG") is not { Length: > 0 } folder)
            return;

        Directory.CreateDirectory(folder);
        Paths.OverrideBase(Directory.CreateTempSubdirectory("NimbusWidgetDemo").FullName);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Loc.Use(Environment.GetEnvironmentVariable("NIMBUS_WIDGET_LANG") ?? "en");
                var app = new Nimbus.App();
                app.InitializeComponent();
                const string stats = "CPU 19%  -  GPU 12%  -  8:22 PM  -  Session 12:35";
                var song = new Nimbus.Integrations.MediaState("Midnight City", "M83", "Spotify.exe", null, true,
                    TimeSpan.FromMinutes(1.6), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4.1), true, Nimbus.Integrations.RepeatMode.None, true, true, true, true);

                var card = new MusicOverlay(Process.GetCurrentProcess());
                card.Display(song);
                card.SetStats(stats);
                card.MusicPart.Visibility = Visibility.Visible;
                card.StatsPart.Visibility = Visibility.Visible;
                var full = Render(card);

                card.MusicPart.Visibility = Visibility.Collapsed;
                card.StatsPart.BorderThickness = new Thickness(0);
                card.StatsPart.Margin = new Thickness(0);
                var statsOnly = Render(card);

                var menu = new WidgetMenu { Left = -20000, Top = -20000, Width = 1280, Height = 720, ShowActivated = false };
                menu.Fill(new WidgetSettings { Shown = [WidgetKind.Music, WidgetKind.Cpu, WidgetKind.Gpu, WidgetKind.Clock, WidgetKind.Session] });
                menu.Show();
                Wait(TimeSpan.FromSeconds(2));

                var background = new LinearGradientBrush(Color.FromRgb(0x3B, 0x5B, 0x7A), Color.FromRgb(0x1E, 0x2A, 0x22), 90);
                foreach (var (name, withMenu, cardImage) in new[] { ("card.png", false, full), ("card-stats-only.png", false, statsOnly), ("menu.png", true, full) })
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(background, null, new System.Windows.Rect(0, 0, 1280, 720));
                        if (withMenu)
                        {
                            var menuImage = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
                            menuImage.Render((FrameworkElement)menu.Content);
                            dc.DrawImage(menuImage, new System.Windows.Rect(0, 0, 1280, 720));
                        }
                        dc.DrawImage(cardImage, new System.Windows.Rect(1280 - 16 - cardImage.Width, 64, cardImage.Width, cardImage.Height));
                    }
                    var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    Save(bitmap, Path.Combine(folder, name));
                }
                menu.Close();

                var toast = new GameToast(Process.GetCurrentProcess());
                toast.TitleText.Text = Loc.T("Watcher.ServerLocation");
                toast.BodyText.Text = "Miami, United States";
                Save(Render(toast), Path.Combine(folder, "server.png"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static BitmapSource Render(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new System.Windows.Rect(content.DesiredSize));
        content.UpdateLayout();
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        window.Content = content;
        return image;
    }

    /// <summary>NIMBUS_WIDGET_PNG=&lt;folder&gt;: the panel that opens while the mouse is on Play in the top bar.</summary>
    [Fact]
    public void RenderPlayPanel()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_WIDGET_PNG") is not { Length: > 0 } folder)
            return;

        Directory.CreateDirectory(folder);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); // like the real UI thread
                var app = new Nimbus.App();
                app.InitializeComponent();
                string home = Directory.CreateTempSubdirectory("NimbusPlayDemo").FullName;
                var settings = new JsonStore<Settings>(Path.Combine(home, "Settings.json"), NimbusJson.Default.Settings);
                var state = new JsonStore<AppState>(Path.Combine(home, "State.json"), NimbusJson.Default.AppState);
                typeof(App).GetProperty("SettingsStore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, settings);
                typeof(App).GetProperty("StateStore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, state);
                ThemeManager.Initialize(settings.Value);

                var window = new MainWindow { Left = -20000, Top = -20000, ShowActivated = false };
                window.Show();
                typeof(MainWindow).GetMethod("Play_MouseEnter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [window, null]);
                Wait(TimeSpan.FromSeconds(4)); // the Roblox version check
                ((System.Windows.Controls.Primitives.Popup)window.FindName("PlayPopup")).IsOpen = true; // it only opens itself under a real mouse
                Wait(TimeSpan.FromSeconds(1));

                var panel = (FrameworkElement)window.FindName("PlayPanel");
                var image = new RenderTargetBitmap((int)panel.ActualWidth + 40, (int)panel.ActualHeight + 40, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x1C)), null, new System.Windows.Rect(0, 0, image.Width, image.Height));
                    var inner = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    inner.Render(panel);
                    dc.DrawImage(inner, new System.Windows.Rect(20, 20, inner.Width, inner.Height));
                }
                image.Render(visual);
                Save(image, Path.Combine(folder, "play.png"));
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static void Wait(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Save(BitmapSource bitmap, string file)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
    }
}
