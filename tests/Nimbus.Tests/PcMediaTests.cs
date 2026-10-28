using Nimbus.Integrations;

namespace Nimbus.Tests;

public class PcMediaTests
{
    [Theory]
    [InlineData(RepeatMode.None, RepeatMode.List)]
    [InlineData(RepeatMode.List, RepeatMode.Track)]
    [InlineData(RepeatMode.Track, RepeatMode.None)]
    public void RepeatCycles(RepeatMode current, RepeatMode next) => Assert.Equal(next, PcMedia.NextRepeat(current));

    [Fact]
    public void PositionMovesOnlyWhilePlaying()
    {
        var at = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var playing = new MediaState("t", "a", "app", null, true, TimeSpan.FromSeconds(10), at, TimeSpan.FromSeconds(30), null, null, true, true, false, false);

        Assert.Equal(TimeSpan.FromSeconds(15), playing.PositionNow(at.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(30), playing.PositionNow(at.AddMinutes(5))); // never past the end
        Assert.Equal(TimeSpan.FromSeconds(10), (playing with { IsPlaying = false }).PositionNow(at.AddSeconds(5)));
    }

    /// <summary>NIMBUS_LIVE_MEDIA=1: reads what's really playing on this PC through the raw WinRT calls.</summary>
    [Fact]
    public async Task ReadsTheRealSession()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_LIVE_MEDIA") != "1")
            return;

        for (int i = 0; i < 20; i++) // also checks nothing leaks or crashes when called repeatedly
        {
            var state = await PcMedia.GetAsync();
            Assert.NotNull(state);
            Assert.False(string.IsNullOrEmpty(state.Title));
            Assert.True(state.Duration > TimeSpan.Zero);
            if (i == 0)
                Console.WriteLine($"LIVE: {state.App} | {state.Title} | {state.Artist} | playing={state.IsPlaying} pos={state.Position} / {state.Duration} shuffle={state.Shuffle} repeat={state.Repeat} next={state.CanNext} prev={state.CanPrevious} cover={state.Cover?.Length ?? 0} bytes, starts {(state.Cover is { } c ? Convert.ToHexString(c, 0, Math.Min(16, c.Length)) : "-")}");
            if (i == 0 && state.Cover is { } bytes && Environment.GetEnvironmentVariable("NIMBUS_COVER_FILE") is { Length: > 0 } coverFile)
                File.WriteAllBytes(coverFile, bytes);
        }
    }
}
