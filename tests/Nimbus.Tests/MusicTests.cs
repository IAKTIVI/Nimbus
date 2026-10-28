using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nimbus.Integrations;
using Nimbus.UI;

namespace Nimbus.Tests;

public class MusicTests
{
    [Theory]
    [InlineData(61, "1:01")]
    [InlineData(5, "0:05")]
    [InlineData(3725, "1:02:05")]
    public void TimesLikeAPlayer(int seconds, string text) => Assert.Equal(text, MusicOverlay.Format(TimeSpan.FromSeconds(seconds)));

    /// <summary>
    /// NIMBUS_CARD_PNG=&lt;file&gt;: renders the card to a PNG for a visual check, with what's really playing
    /// on this PC (NIMBUS_LIVE_MEDIA=1) or a sample browser video without shuffle/repeat.
    /// </summary>
    [Fact]
    public void RenderCard()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_CARD_PNG") is not { Length: > 0 } output)
            return;

        MediaState? state = Environment.GetEnvironmentVariable("NIMBUS_LIVE_MEDIA") == "1"
            ? PcMedia.GetAsync().GetAwaiter().GetResult()
            : null;
        state ??= new MediaState("Watch Hunter x Hunter (2011)", "Miruro", "chrome.exe", null, false,
            TimeSpan.FromMinutes(22.2), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(24), null, null, false, true, false, false);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Nimbus.App();
                app.InitializeComponent();

                var card = new MusicOverlay(Process.GetCurrentProcess());
                card.Display(state);
                Console.WriteLine($"COVER: bytes={state.Cover?.Length ?? 0} source={card.Cover.Source?.GetType().Name ?? "null"} {card.Cover.Source?.Width}x{card.Cover.Source?.Height}");
                var content = (System.Windows.FrameworkElement)card.Content;
                card.Content = null;
                content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                content.Arrange(new System.Windows.Rect(content.DesiredSize));
                content.UpdateLayout();

                var bitmap = new RenderTargetBitmap((int)content.ActualWidth * 2, (int)content.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(output);
                encoder.Save(file);
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
}

public class LiveCardTests
{
    /// <summary>
    /// NIMBUS_LIVE_CARD=1 with Roblox and music running: puts the real card on the real Roblox window for a few
    /// seconds and checks it reads the music and sits in Roblox's top-right corner.
    /// </summary>
    [Fact]
    public void CardWorksOnTheRunningRoblox()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_LIVE_CARD") != "1")
            return;

        var roblox = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault(p => p.MainWindowHandle != 0);
        Assert.NotNull(roblox);

        string? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Nimbus.App();
                app.InitializeComponent();
                var card = new Nimbus.UI.MusicOverlay(roblox) { AssumeRobloxActive = true };
                card.Start();

                var frame = new System.Windows.Threading.DispatcherFrame();
                var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                stop.Tick += (_, _) =>
                {
                    stop.Stop();
                    result = $"shown={card.IsOnScreen} placement={card.LastPlacement} song={card.Current?.Title} | {card.Current?.Artist} | app={card.Current?.App} cover={card.Current?.Cover?.Length}";
                    card.Close();
                    frame.Continue = false;
                };
                stop.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine("LIVECARD: " + result);
        Assert.Null(failure);
        Assert.Contains("shown=True", result);
    }
}

public class LiveToastTests
{
    /// <summary>
    /// NIMBUS_LIVE_CARD=1 with Roblox running: looks up the real server from Roblox's newest log and shows the
    /// location card on the Roblox window for a few seconds.
    /// </summary>
    [Fact]
    public async Task LocationCardWorksOnTheRunningRoblox()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_LIVE_CARD") != "1")
            return;

        var roblox = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").First(p => p.MainWindowHandle != 0);
        string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
        string? ip = null;
        foreach (var file in new DirectoryInfo(logs).EnumerateFiles("*_Player_*.log").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            using var reader = new StreamReader(new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            while (reader.ReadLine() is { } line)
                if (Nimbus.Integrations.LogParser.Parse(line) is Nimbus.Integrations.LogEvent.ServerAddress server)
                    ip = server.Ip;
            if (ip is not null)
                break;
        }
        Assert.NotNull(ip);
        string? location = await Nimbus.Integrations.RobloxApi.GetServerLocationAsync(ip!);
        Assert.NotNull(location);

        string? result = null;
        var thread = new Thread(() =>
        {
            var app = System.Windows.Application.Current as Nimbus.App ?? new Nimbus.App();
            if (app.Resources.MergedDictionaries.Count == 0)
                app.InitializeComponent();
            var toast = new Nimbus.UI.GameToast(roblox) { AssumeRobloxActive = true };
            toast.Start();
            toast.Post("Server location", location!, "\uE707", TimeSpan.FromSeconds(4));

            var frame = new System.Windows.Threading.DispatcherFrame();
            var check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            int ticks = 0;
            check.Tick += (_, _) =>
            {
                ticks++;
                if (ticks == 1)
                    result = $"server={ip} location={location} shown={toast.IsOnScreen} at={toast.LastPosition}";
                else if (ticks == 3)
                {
                    result += $" | after 6s shown={toast.IsOnScreen}";
                    check.Stop();
                    toast.Close();
                    frame.Continue = false;
                }
            };
            check.Interval = TimeSpan.FromSeconds(2);
            check.Start();
            var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine("LIVETOAST: " + result);
        Assert.Contains("shown=True", result);
    }
}

public class CardPlacementTests
{
    [Fact]
    public void DefaultIsTopRightBelowRobloxsBar()
    {
        var (x, y) = Nimbus.UI.CardPlacement.Position(null, left: 0, top: 0, width: 2560, height: 1440, cardWidth: 300, cardHeight: 130, scale: 1);
        Assert.Equal((2244, 64), (x, y));
    }

    [Fact]
    public void MovedCardKeepsItsSpotWhenTheWindowChangesSize()
    {
        // Dropped in the bottom-left corner of a 1920x1080 window...
        var saved = Nimbus.UI.CardPlacement.ToFractions(100, 950, 100, 0, 1920, 1080, 300, 130);
        Assert.Equal((0d, 1d), saved);

        // ...stays in the bottom-left corner of a 2560x1440 window.
        Assert.Equal((0, 1310), Nimbus.UI.CardPlacement.Position(saved, 0, 0, 2560, 1440, 300, 130, 1));
    }

    [Fact]
    public void DroppedOutsideTheWindowSnapsBackInside()
    {
        var saved = Nimbus.UI.CardPlacement.ToFractions(-500, 5000, 0, 0, 1920, 1080, 300, 130);
        Assert.Equal((0d, 1d), saved);
    }

    [Theory]
    [InlineData(-10, 274, 0)]
    [InlineData(137, 274, 0.5)]
    [InlineData(400, 274, 1)]
    [InlineData(50, 0, 0)]
    public void SeekFraction(double x, double width, double expected) =>
        Assert.Equal(expected, Nimbus.UI.MusicOverlay.SeekFraction(x, width), 3);
}
