using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.Tests;

public class CustomAppTests
{
    [Theory]
    [InlineData("https://www.roblox.com/games/920587237/Adopt-Me", 920587237L)]
    [InlineData("https://www.roblox.com/de/games/4924922222/Brookhaven-RP", 4924922222L)]
    [InlineData("roblox.com/games/606849621", 606849621L)]
    [InlineData("roblox://experiences/start?placeId=1818&gameInstanceId=abc", 1818L)]
    [InlineData("  2753915549  ", 2753915549L)]
    [InlineData("https://www.roblox.com/users/1/profile", null)]
    [InlineData("hello", null)]
    [InlineData("0", null)]
    public void ReadsPlaceIdsFromGameLinks(string link, long? placeId) =>
        Assert.Equal(placeId, CustomAppRunner.ParseGameLink(link));

    [Fact]
    public void GamesMatchByPlaceOrExperience()
    {
        var app = new CustomApp { Games = [new CustomAppGame { PlaceId = 100, UniverseId = 7 }, new CustomAppGame { PlaceId = 200 }] };

        Assert.True(app.WantsGame(100, null));
        Assert.True(app.WantsGame(555, 7)); // another place of the same experience (a teleport)
        Assert.True(app.WantsGame(200, 9));
        Assert.False(app.WantsGame(300, 9));
        Assert.False(app.WantsGame(300, 0)); // an unknown experience never matches the 0 of a game added offline
        Assert.False(app.WantsGame(300, null));
    }

    [Fact]
    public void CustomAppsAreSavedAndLoaded()
    {
        string file = Path.Combine(Directory.CreateTempSubdirectory("NimbusApps").FullName, "Settings.json");
        var store = new JsonStore<Settings>(file, NimbusJson.Default.Settings);
        store.Load();
        store.Value.Integrations.CustomApps.Add(new CustomApp
        {
            Path = @"C:\Tools\Macro.exe",
            Arguments = "--quiet",
            DelaySeconds = 10,
            CloseWithRoblox = true,
            Games = [new CustomAppGame { PlaceId = 1818, UniverseId = 13058, Name = "Classic: Crossroads" }],
        });
        store.Save();

        var app = Assert.Single(new JsonStore<Settings>(file, NimbusJson.Default.Settings).Load().Integrations.CustomApps);
        Assert.Equal("Macro", app.Name);
        Assert.Equal("--quiet", app.Arguments);
        Assert.Equal(10, app.DelaySeconds);
        Assert.True(app.CloseWithRoblox && app.Enabled);
        Assert.Equal(13058, Assert.Single(app.Games).UniverseId);
        Assert.DoesNotContain("\"Name\": \"Macro\"", File.ReadAllText(file));
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }

    [Fact]
    public void ProgramsKeepTheWatcherRunning()
    {
        var settings = new Settings();
        settings.Integrations = new IntegrationSettings { ServerLocation = false, GameHistory = false, MusicCard = false, DiscordStatus = false };
        var noWidgets = new WidgetSettings();
        Assert.False(ActivityWatcher.IsNeeded(settings, noWidgets));

        settings.Integrations.CustomApps.Add(new CustomApp { Path = @"C:\x.exe" });
        Assert.True(ActivityWatcher.IsNeeded(settings, noWidgets));

        settings.Integrations.CustomApps[0].Enabled = false;
        Assert.False(ActivityWatcher.IsNeeded(settings, noWidgets));
        Assert.True(ActivityWatcher.IsNeeded(settings, new WidgetSettings { Shown = [WidgetKind.Clock] }));
    }

    [Fact]
    public void LanguageFollowsWindowsByDefault() => Assert.Null(new Settings().Language);
}
