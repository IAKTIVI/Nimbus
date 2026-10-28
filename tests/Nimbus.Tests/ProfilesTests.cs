using System.Text.Json;
using Nimbus.Core;

namespace Nimbus.Tests;

public class ProfilesTests
{
    [Fact]
    public void ApplyingAProfileKeepsTheFullscreenChoice()
    {
        var settings = new Settings { Engine = new EngineSettings { ExclusiveFullscreen = true } };

        Profiles.Apply(settings, Profiles.Potato);

        Assert.True(settings.Engine.ExclusiveFullscreen);
        Assert.True(settings.Engine.GraySky);
        Assert.Same(Profiles.Potato, Profiles.Current(settings));
    }

    [Fact]
    public void ApplyingDoesNotShareTheProfilesObject()
    {
        var settings = new Settings();
        Profiles.Apply(settings, Profiles.Quality);
        settings.Engine.GraySky = true;

        Assert.False(Profiles.Quality.Engine.GraySky);
    }

    [Fact]
    public void ChangingAnOptionMakesItCustom()
    {
        var settings = new Settings();
        Profiles.Apply(settings, Profiles.Balanced);
        settings.Engine = settings.Engine with { GraySky = true };

        Assert.Null(Profiles.Current(settings));
    }

    [Fact]
    public void CustomProfilesAreRecognised()
    {
        var mine = new Profile("Mine", new EngineSettings { HideGrass = true, Msaa = 2 });
        var settings = new Settings { Profiles = [mine], Engine = new EngineSettings { HideGrass = true, Msaa = 2 } };

        Assert.Equal(mine, Profiles.Current(settings));
    }

    [Theory]
    [InlineData("", "Give the profile a name.")]
    [InlineData("potato", "A profile with that name already exists.")]
    [InlineData("Mine", "A profile with that name already exists.")]
    [InlineData("Ranked", null)]
    public void NameValidation(string name, string? expected)
    {
        var settings = new Settings { Profiles = [new Profile("Mine", new EngineSettings())] };
        Assert.Equal(expected, Profiles.ValidateName(settings, name));
    }

    [Fact]
    public void RenamingToTheSameNameIsAllowed()
    {
        var settings = new Settings { Profiles = [new Profile("Mine", new EngineSettings())] };
        Assert.Null(Profiles.ValidateName(settings, "MINE", renaming: "Mine"));
    }

    [Fact]
    public void SummaryListsTheChanges()
    {
        Assert.Equal("Roblox defaults", Profiles.Summarize(new EngineSettings()));
        Assert.Equal("AA: Off - Textures: Lowest - Gray sky", Profiles.Summarize(new EngineSettings { Msaa = 1, TextureQuality = 0, GraySky = true }));
    }

    [Fact]
    public void SettingsWithProfilesSurviveSaving()
    {
        var settings = new Settings
        {
            Engine = new EngineSettings { GraphicsApi = GraphicsApi.Vulkan, QualityLevel = 5 },
            CustomFlags = "{ }",
            Profiles = [new Profile("Mine", new EngineSettings { HideGrass = true })],
        };

        string json = JsonSerializer.Serialize(settings, NimbusJson.Default.Settings);
        var back = JsonSerializer.Deserialize(json, NimbusJson.Default.Settings)!;

        Assert.Equal(settings.Engine, back.Engine);
        Assert.Equal(settings.Profiles, back.Profiles);
        Assert.Contains("\"Vulkan\"", json);
    }
}
