using Nimbus.Core;
using Nimbus.Integrations;
using Nimbus.UI;

namespace Nimbus.Tests;

public class WidgetTests
{
    [Fact]
    public void StatsShowInOneLineInAFixedOrder()
    {
        string? Value(WidgetKind kind) => kind switch
        {
            WidgetKind.Cpu => "CPU 19%",
            WidgetKind.Gpu => "GPU 12%",
            WidgetKind.Clock => "20:22",
            WidgetKind.Session => null, // not in a game yet
            _ => null,
        };

        Assert.Equal("CPU 19%  -  GPU 12%  -  20:22", WidgetHost.StatsLine([WidgetKind.Clock, WidgetKind.Session, WidgetKind.Gpu, WidgetKind.Cpu], Value));
        Assert.Null(WidgetHost.StatsLine([WidgetKind.Music, WidgetKind.Session], Value));
    }

    [Fact]
    public void PingIsNoLongerOffered() => Assert.DoesNotContain(WidgetKind.Ping, WidgetHost.Choices);

    [Theory]
    [InlineData(59, "00:59")]
    [InlineData(61 * 60 + 5, "1:01:05")]
    public void SessionTimeLooksLikeAStopwatch(int seconds, string expected) =>
        Assert.Equal(expected, WidgetHost.SessionTime(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("0.745.0.7451137_20261028T191623Z_Player_B789B_last.log", true)]
    [InlineData("0.745.0.7451137_20261028T191623Z_Player_B789B_CrashHandler_last.log", false)]
    [InlineData("0.745.0.7451137_20261028T191623Z_Studio_B789B_last.log", false)]
    public void FollowsTheGameLogNotTheCrashHandler(string file, bool followed) =>
        Assert.Equal(followed, ActivityWatcher.IsGameLog(file));

    [Fact]
    public void Defaults()
    {
        var fresh = new WidgetSettings();
        Assert.Empty(fresh.Shown);
        Assert.Equal(0xA3, fresh.MenuKey); // Right Ctrl
        Assert.True(fresh.DimGame && fresh.Snow);
    }

    [Fact]
    public void SystemLoadReadsSomething()
    {
        using var load = new SystemLoad();
        Thread.Sleep(300);
        Assert.InRange(load.Cpu() ?? 0, 0, 100);
        Assert.InRange(load.Gpu() ?? 0, 0, 100);
    }
}
