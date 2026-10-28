using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.Tests;

public class PlaytimeTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SessionsOfOneExperienceAddUp()
    {
        var totals = new Dictionary<string, PlaytimeEntry>();
        string key = Playtime.Key(100, 7);

        Assert.True(Playtime.Record(totals, key, 100, "Adopt Me!", "icon", Start, Start.AddMinutes(30)));
        Assert.True(Playtime.Record(totals, Playtime.Key(555, 7), 555, "Adopt Me!", null, Start.AddHours(1), Start.AddHours(2)));

        var entry = Assert.Single(totals).Value;
        Assert.Equal(90 * 60, entry.Seconds);
        Assert.Equal(2, entry.Sessions);
        Assert.Equal("icon", entry.IconUrl); // a session without an icon keeps the one it had
        Assert.Equal(Start.AddHours(2), entry.LastPlayed);
    }

    [Fact]
    public void RemovingOneGameKeepsTheOthers()
    {
        var totals = new Dictionary<string, PlaytimeEntry>();
        Playtime.Record(totals, Playtime.Key(100, 7), 100, "Adopt Me!", null, Start, Start.AddHours(1));
        Playtime.Record(totals, Playtime.Key(200, null), 200, "Brookhaven", null, Start, Start.AddHours(1));

        Assert.True(Playtime.Remove(totals, 100));
        Assert.Equal("Brookhaven", Assert.Single(totals).Value.Name);
        Assert.False(Playtime.Remove(totals, 100));
    }

    [Fact]
    public void TooShortOrImpossibleSessionsDontCount()
    {
        var totals = new Dictionary<string, PlaytimeEntry>();
        Assert.False(Playtime.Record(totals, "p1", 1, "x", null, Start, Start.AddSeconds(10)));
        Assert.False(Playtime.Record(totals, "p1", 1, "x", null, Start, Start.AddDays(3)));
        Assert.False(Playtime.Record(totals, "p1", 1, "x", null, Start, Start.AddMinutes(-5)));
        Assert.Empty(totals);
    }

    [Fact]
    public void HistoryGivesAHeadStart()
    {
        var totals = new Dictionary<string, PlaytimeEntry>();
        Playtime.ImportHistory(totals,
        [
            new HistoryEntry { PlaceId = 1, Name = "A", JoinedAt = Start, LeftAt = Start.AddMinutes(10) },
            new HistoryEntry { PlaceId = 1, Name = "A", JoinedAt = Start.AddHours(1), LeftAt = Start.AddHours(1).AddMinutes(20) },
            new HistoryEntry { PlaceId = 2, Name = "B", JoinedAt = Start }, // still running / never ended: skipped
        ]);

        var a = Assert.Single(totals).Value;
        Assert.Equal(30 * 60, a.Seconds);
        Assert.Equal(2, a.Sessions);
    }

    [Fact]
    public void PlacesOfOneGameBecomeOneEntry()
    {
        var totals = new Dictionary<string, PlaytimeEntry>
        {
            ["p3095204897"] = new() { PlaceId = 3095204897, Name = "Isle", Seconds = 600, Sessions = 2, LastPlayed = Start },
            ["p3431407618"] = new() { PlaceId = 3431407618, Name = "Isle", Seconds = 3000, Sessions = 3, LastPlayed = Start.AddHours(1) },
            ["u777"] = new() { PlaceId = 3095204897, Name = "Isle", Seconds = 1200, Sessions = 1, LastPlayed = Start.AddHours(2) },
            ["p1"] = new() { PlaceId = 1, Name = "Other", Seconds = 60, Sessions = 1 },
        };
        var isle = new GameDetails(0, 777, "Isle", null, "icon", RootPlaceId: 3095204897);

        Assert.True(Playtime.Merge(totals, new Dictionary<long, GameDetails> { [3095204897] = isle, [3431407618] = isle }));

        Assert.Equal(2, totals.Count); // Isle once, the unknown game untouched
        var whole = totals["u777"];
        Assert.Equal(4800, whole.Seconds);
        Assert.Equal(6, whole.Sessions);
        Assert.Equal(3095204897, whole.PlaceId); // Play starts at the lobby
        Assert.Equal(Start.AddHours(2), whole.LastPlayed);
        Assert.False(Playtime.Merge(totals, new Dictionary<long, GameDetails> { [3095204897] = isle }));
    }

    [Theory]
    [InlineData(20, "under a minute")]
    [InlineData(45 * 60, "45 min")]
    [InlineData(3 * 3600 + 20 * 60 + 59, "3 h 20 min")]
    public void FormatsTotals(long seconds, string expected) => Assert.Equal(expected, Playtime.Format(seconds));

    [Fact]
    public void UpdatesStayInEnglishNewInstallsFollowWindows()
    {
        Assert.Equal("en", Loc.FirstLanguage(existingUser: true));
        Assert.Equal(Loc.SystemDefault, Loc.FirstLanguage(existingUser: false));
    }
}
