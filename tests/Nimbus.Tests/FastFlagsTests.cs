using System.Text.Json;
using Nimbus.Core;
using Nimbus.Roblox;

namespace Nimbus.Tests;

public sealed class FastFlagsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("NimbusFlagsTest").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DefaultsWriteNoFlags() =>
        Assert.Empty(FastFlags.FromEngine(new EngineSettings()));

    [Fact]
    public void EveryToggleOnlyUsesAllowlistedFlags()
    {
        var everything = new EngineSettings
        {
            GraphicsApi = GraphicsApi.Vulkan,
            Msaa = 4,
            TextureQuality = 2,
            QualityLevel = 10,
            GraySky = true,
            HideGrass = true,
            StillGrass = true,
            LowDetailMeshes = true,
            FreezeLighting = true,
            ExclusiveFullscreen = true,
            FullResolution = true,
        };

        var flags = FastFlags.FromEngine(everything);

        // All 18 allowlisted flags, minus the two graphics APIs that weren't picked.
        Assert.Equal(16, flags.Count);
        Assert.All(flags.Keys, name => Assert.Contains(name, FastFlags.Allowlist));
    }

    [Theory]
    [InlineData(GraphicsApi.Direct3D11, "FFlagDebugGraphicsPreferD3D11")]
    [InlineData(GraphicsApi.Vulkan, "FFlagDebugGraphicsPreferVulkan")]
    [InlineData(GraphicsApi.OpenGL, "FFlagDebugGraphicsPreferOpenGL")]
    public void GraphicsApiSetsExactlyOneFlag(GraphicsApi api, string flag)
    {
        var flags = FastFlags.FromEngine(new EngineSettings { GraphicsApi = api });
        Assert.Equal("True", Assert.Single(flags, f => f.Key.StartsWith("FFlagDebugGraphicsPrefer")).Value);
        Assert.True(flags.ContainsKey(flag));
    }

    [Fact]
    public void PotatoProfileFlags()
    {
        var flags = FastFlags.FromEngine(Profiles.Potato.Engine);

        Assert.Equal("1", flags["FIntDebugForceMSAASamples"]);
        Assert.Equal("True", flags["DFFlagTextureQualityOverrideEnabled"]);
        Assert.Equal("0", flags["DFIntTextureQualityOverride"]);
        Assert.Equal("1", flags["DFIntDebugFRMQualityLevelOverride"]);
        Assert.Equal("0", flags["FIntFRMMaxGrassDistance"]);
        Assert.Equal("True", flags["DFFlagDebugPauseVoxelizer"]);
        Assert.False(flags.ContainsKey("DFFlagDisableDPIScale"));
    }

    [Fact]
    public void ExclusiveFullscreenTurnsManualHandlingOff() =>
        Assert.Equal("False", FastFlags.FromEngine(new EngineSettings { ExclusiveFullscreen = true })["FFlagHandleAltEnterFullscreenManually"]);

    [Fact]
    public void ParsesTextNumbersAndBooleans()
    {
        var flags = FastFlags.ParseCustom("""
            {
              // comments and trailing commas are fine
              "FFlagDebugSkyGray": true,
              "FIntDebugForceMSAASamples": 2,
              "DFIntTaskSchedulerTargetFps": "9999",
            }
            """);

        Assert.Equal(new CustomFlag("FFlagDebugSkyGray", "True", Allowed: true), flags[0]);
        Assert.Equal(new CustomFlag("FIntDebugForceMSAASamples", "2", Allowed: true), flags[1]);
        Assert.Equal(new CustomFlag("DFIntTaskSchedulerTargetFps", "9999", Allowed: false), flags[2]);
    }

    [Fact]
    public void EmptyEditorIsFine() => Assert.Empty(FastFlags.ParseCustom("   "));

    [Theory]
    [InlineData("{ \"FFlagDebugSkyGray\": ")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"FFlagDebugSkyGray\": [true] }")]
    public void InvalidEditorJsonThrows(string json) =>
        Assert.ThrowsAny<Exception>(() => FastFlags.ParseCustom(json));

    [Fact]
    public void AdvancedFlagsOverrideToggles()
    {
        var settings = new Settings
        {
            Engine = new EngineSettings { Msaa = 4 },
            CustomFlags = """{ "FIntDebugForceMSAASamples": "1" }""",
        };

        Assert.Equal("1", FastFlags.Build(settings)["FIntDebugForceMSAASamples"]);
    }

    [Fact]
    public void BrokenAdvancedJsonStillKeepsToggles()
    {
        var settings = new Settings { Engine = new EngineSettings { GraySky = true }, CustomFlags = "{ oops" };
        Assert.Equal("True", FastFlags.Build(settings)["FFlagDebugSkyGray"]);
    }

    [Fact]
    public void WritesClientAppSettingsAsStrings()
    {
        var settings = new Settings { Engine = new EngineSettings { Msaa = 2, GraySky = true } };

        FastFlags.Apply(_dir, settings);

        string path = Path.Combine(_dir, "ClientSettings", "ClientAppSettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("2", doc.RootElement.GetProperty("FIntDebugForceMSAASamples").GetString());
        Assert.Equal("True", doc.RootElement.GetProperty("FFlagDebugSkyGray").GetString());
    }

    [Fact]
    public void ApplyReplacesOldFlags()
    {
        FastFlags.Apply(_dir, new Settings { Engine = new EngineSettings { GraySky = true } });
        FastFlags.Apply(_dir, new Settings());

        Assert.Equal("{}", File.ReadAllText(Path.Combine(_dir, "ClientSettings", "ClientAppSettings.json")).Trim());
    }
}
