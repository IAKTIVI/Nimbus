using System.Text;
using System.Text.Json;
using Nimbus.Core;
using Nimbus.Mods;

namespace Nimbus.Tests;

/// <summary>Tests that point the global Paths at a temp folder run one at a time.</summary>
[CollectionDefinition("Paths", DisableParallelization = true)]
public class PathsCollection;

[Collection("Paths")]
public sealed class ModTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("NimbusModTest").FullName;
    private readonly string _originalBase = Paths.Base;

    public ModTests() => Paths.OverrideBase(Path.Combine(_root, "Nimbus"));

    public void Dispose()
    {
        Paths.OverrideBase(_originalBase);
        Directory.Delete(_root, recursive: true);
    }

    private string Version => Path.Combine(_root, "version-0123456789abcdef");

    private string Backups => Path.Combine(_root, "backups");

    private void RobloxFile(string rel, string content)
    {
        string path = Path.Combine(Version, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string rel) => File.ReadAllText(Path.Combine(Version, rel));

    private bool Exists(string rel) => File.Exists(Path.Combine(Version, rel));

    private string ExternalFile(string name, string content)
    {
        string path = Path.Combine(_root, "picked", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AppliesThenRestoresEverything()
    {
        RobloxFile(@"content\sounds\ouch.ogg", "new ouch");
        RobloxFile(@"PlatformContent\pc\textures\water\normal_01.dds", "waves");
        var applier = new ModApplier(Version, Backups);

        var result = applier.Apply(new Dictionary<string, ModFile>
        {
            [@"content\sounds\ouch.ogg"] = new ModFile.Write(Encoding.UTF8.GetBytes("oof")),
            [@"PlatformContent\pc\textures\water\normal_01.dds"] = new ModFile.Delete(),
            [@"content\added.txt"] = new ModFile.Copy(ExternalFile("added.txt", "mine")),
        });

        Assert.Equal(new ModResult(3, 0, 0), result);
        Assert.Equal("oof", Read(@"content\sounds\ouch.ogg"));
        Assert.False(Exists(@"PlatformContent\pc\textures\water\normal_01.dds"));
        Assert.Equal("mine", Read(@"content\added.txt"));

        // A fresh applier (= next launch) with nothing enabled puts Roblox back exactly as it was.
        var restored = new ModApplier(Version, Backups).Apply(new Dictionary<string, ModFile>());

        Assert.Equal(new ModResult(0, 3, 0), restored);
        Assert.Equal("new ouch", Read(@"content\sounds\ouch.ogg"));
        Assert.Equal("waves", Read(@"PlatformContent\pc\textures\water\normal_01.dds"));
        Assert.False(Exists(@"content\added.txt"));
    }

    [Fact]
    public void ApplyingTheSameModsAgainChangesNothing()
    {
        RobloxFile(@"content\a.txt", "original");
        var mods = new Dictionary<string, ModFile>
        {
            [@"content\a.txt"] = new ModFile.Copy(ExternalFile("a.txt", "modded")),
            [@"content\b.txt"] = new ModFile.Write("written"u8.ToArray()),
        };

        new ModApplier(Version, Backups).Apply(mods);
        var second = new ModApplier(Version, Backups).Apply(mods);

        Assert.Equal(new ModResult(0, 0, 0), second);
    }

    [Fact]
    public void OriginalsStayReadableWhileModded()
    {
        RobloxFile(@"content\sounds\oof.ogg", "classic");
        RobloxFile(@"PlatformContent\pc\textures\water\normal_01.dds", "1");
        RobloxFile(@"PlatformContent\pc\textures\water\normal_02.dds", "2");
        var applier = new ModApplier(Version, Backups);
        applier.Apply(new Dictionary<string, ModFile>
        {
            [@"content\sounds\oof.ogg"] = new ModFile.Write("other"u8.ToArray()),
            [@"PlatformContent\pc\textures\water\normal_01.dds"] = new ModFile.Delete(),
        });

        var later = new ModApplier(Version, Backups);
        Assert.Equal("classic"u8.ToArray(), later.ReadOriginal(@"content\sounds\oof.ogg"));
        Assert.Equal(2, later.ListOriginals(ModManager.WaterFolder, "*.dds").Count);
    }

    [Fact]
    public void RefusesPathsOutsideTheRobloxFolder()
    {
        Directory.CreateDirectory(Version);
        var result = new ModApplier(Version, Backups).Apply(new Dictionary<string, ModFile>
        {
            [@"..\evil.txt"] = new ModFile.Write("x"u8.ToArray()),
        });

        Assert.Equal(1, result.Failed);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Fact]
    public void WaterModRemovesEveryWaveTexture()
    {
        for (int i = 1; i <= 25; i++)
            RobloxFile($@"PlatformContent\pc\textures\water\normal_{i:00}.dds", "w");
        var applier = new ModApplier(Version, Backups);

        var mods = ModManager.Build(new ModSettings { RemoveWaterTexture = true }, applier, Paths.Modifications);
        applier.Apply(mods);

        Assert.Equal(25, mods.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(Version, ModManager.WaterFolder)));

        // Next launch still knows about all 25, even though they're gone from the folder.
        var next = new ModApplier(Version, Backups);
        Assert.Equal(25, ModManager.Build(new ModSettings { RemoveWaterTexture = true }, next, Paths.Modifications).Count);
    }

    [Fact]
    public void ClassicSoundAndCursorUseRobloxsOwnFiles()
    {
        RobloxFile(ModManager.ClassicDeathSound, "oof");
        RobloxFile(ModManager.DeathSoundFile, "ouch");
        foreach (var (current, classic) in ModManager.CursorFiles)
        {
            RobloxFile(current, "new " + Path.GetFileName(current));
            RobloxFile(classic, "old " + Path.GetFileName(classic));
        }

        var applier = new ModApplier(Version, Backups);
        applier.Apply(ModManager.Build(new ModSettings { DeathSound = DeathSoundChoice.Classic, Cursor = CursorChoice.Classic }, applier, Paths.Modifications));

        Assert.Equal("oof", Read(ModManager.DeathSoundFile));
        Assert.Equal("old ArrowCursor.png", Read(@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png"));
        Assert.Equal("old ArrowFarCursor.png", Read(@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png"));
    }

    [Fact]
    public void CustomFontRewritesEveryFamily()
    {
        RobloxFile(@"content\fonts\families\Arimo.json", """
            { "name": "Arimo", "faces": [
              { "name": "Regular", "weight": 400, "style": "normal", "assetId": "rbxasset://fonts/Arimo-Regular.ttf" },
              { "name": "Bold", "weight": 700, "style": "normal", "assetId": "rbxassetid://16658205823" } ] }
            """);
        Directory.CreateDirectory(Paths.ModAssets);
        File.WriteAllText(Path.Combine(Paths.ModAssets, "CustomFont.otf"), "font bytes");

        var applier = new ModApplier(Version, Backups);
        applier.Apply(ModManager.Build(new ModSettings { UseCustomFont = true }, applier, Paths.Modifications));

        Assert.Equal("font bytes", Read(@"content\fonts\CustomFont.otf"));
        using var family = JsonDocument.Parse(Read(@"content\fonts\families\Arimo.json"));
        Assert.All(family.RootElement.GetProperty("faces").EnumerateArray(),
            face => Assert.Equal("rbxasset://fonts/CustomFont.otf", face.GetProperty("assetId").GetString()));
        Assert.Equal("Arimo", family.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void UserModsFolderIsAppliedButNotClientSettings()
    {
        string folder = Paths.Modifications;
        Directory.CreateDirectory(Path.Combine(folder, @"content\sounds"));
        Directory.CreateDirectory(Path.Combine(folder, "ClientSettings"));
        File.WriteAllText(Path.Combine(folder, @"content\sounds\jump.mp3"), "boing");
        File.WriteAllText(Path.Combine(folder, @"ClientSettings\ClientAppSettings.json"), "{}");

        var mods = ModManager.Build(new ModSettings(), new ModApplier(Version, Backups), folder);

        Assert.True(mods.ContainsKey(@"content\sounds\jump.mp3"));
        Assert.Single(mods);
    }

    /// <summary>
    /// Runs every built-in mod against copies of a real Roblox install (NIMBUS_REAL_ROBLOX=&lt;version folder&gt;),
    /// then turns them all off and checks Roblox's files are back byte for byte.
    /// </summary>
    [Fact]
    public void RealRobloxFilesSurviveAModRoundTrip()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_REAL_ROBLOX") is not { Length: > 0 } real)
            return;

        foreach (string folder in new[] { ModManager.WaterFolder, @"content\sounds", @"content\textures\Cursors\KeyboardMouse", ModManager.FontFamilies })
        {
            foreach (string file in Directory.GetFiles(Path.Combine(real, folder)))
                RobloxFileFrom(file, Path.Combine(folder, Path.GetFileName(file)));
        }

        foreach (string classic in ModManager.CursorFiles.Values)
            RobloxFileFrom(Path.Combine(real, classic), classic);

        var before = Snapshot();
        Directory.CreateDirectory(Paths.ModAssets);
        File.Copy(@"C:\Windows\Fonts\segoeui.ttf", Path.Combine(Paths.ModAssets, "CustomFont.ttf"));

        var everything = new ModSettings { RemoveWaterTexture = true, DeathSound = DeathSoundChoice.Classic, Cursor = CursorChoice.Classic, UseCustomFont = true };
        var applier = new ModApplier(Version, Backups);
        var mods = ModManager.Build(everything, applier, Paths.Modifications);
        var result = applier.Apply(mods);

        int families = Directory.GetFiles(Path.Combine(real, ModManager.FontFamilies), "*.json").Length;
        Assert.Equal(0, result.Failed);
        Assert.Equal(25 + 1 + 2 + 1 + families, mods.Count); // water + sound + cursors + font file + families
        Assert.Empty(Directory.GetFiles(Path.Combine(Version, ModManager.WaterFolder)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(real, ModManager.ClassicDeathSound)), File.ReadAllBytes(Path.Combine(Version, ModManager.DeathSoundFile)));
        foreach (string family in Directory.GetFiles(Path.Combine(Version, ModManager.FontFamilies)))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(family));
            Assert.All(doc.RootElement.GetProperty("faces").EnumerateArray(),
                face => Assert.Equal("rbxasset://fonts/CustomFont.ttf", face.GetProperty("assetId").GetString()));
        }

        new ModApplier(Version, Backups).Apply(new Dictionary<string, ModFile>());

        Assert.Equal(before, Snapshot());
        Assert.False(File.Exists(Path.Combine(Version, @"content\fonts\CustomFont.ttf")));
    }

    private void RobloxFileFrom(string source, string rel)
    {
        string path = Path.Combine(Version, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(source, path, overwrite: true);
    }

    private SortedDictionary<string, string> Snapshot() => new(
        Directory.GetFiles(Version, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(Version, f),
            f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))),
        StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NimbusDeathSoundsAreSmallWavFiles()
    {
        foreach (var preset in SoundPresets.All)
        {
            byte[] wav = SoundPresets.Render(preset);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
            Assert.InRange(wav.Length, 5_000, 60_000);
            Assert.Contains(wav.Skip(44), b => b != 0); // not silent
        }
    }

    [Fact]
    public void PickedDeathSoundIsApplied()
    {
        RobloxFile(ModManager.DeathSoundFile, "ouch");
        var applier = new ModApplier(Version, Backups);

        var mods = ModManager.Build(new ModSettings { DeathSound = DeathSoundChoice.Nimbus, DeathSoundPreset = "blip" }, applier, Paths.Modifications);
        var write = Assert.IsType<ModFile.Write>(mods[ModManager.DeathSoundFile]);
        Assert.Equal(SoundPresets.Render(SoundPresets.Find("blip")!), write.Content);

        var settings = new ModSettings { DeathSound = DeathSoundChoice.Custom };
        settings.CustomSoundName = ModAssets.AddSound(ExternalFile("fahh.mp3", "fahhh"));
        Assert.Equal("fahh (2).mp3", ModAssets.AddSound(ExternalFile("fahh.mp3", "again"))); // same name: kept apart
        Assert.Equal(["fahh (2).mp3", "fahh.mp3"], ModAssets.OwnSounds(settings));

        applier.Apply(ModManager.Build(settings, applier, Paths.Modifications));
        Assert.Equal("fahhh", Read(ModManager.DeathSoundFile));

        ModAssets.RemoveSound("fahh.mp3");
        Assert.Empty(ModManager.Build(settings, applier, Paths.Modifications));
    }

    [Fact]
    public void OlderSingleDeathSoundMovesIntoTheList()
    {
        Directory.CreateDirectory(Paths.ModAssets);
        File.WriteAllText(Path.Combine(Paths.ModAssets, "DeathSound.ogg"), "old");
        var settings = new ModSettings { DeathSound = DeathSoundChoice.Custom, CustomSoundName = "scream.ogg" };

        Assert.Equal(Path.Combine(ModAssets.SoundsFolder, "scream.ogg"), ModAssets.SelectedSound(settings));
        Assert.False(File.Exists(Path.Combine(Paths.ModAssets, "DeathSound.ogg")));
    }

    [Fact]
    public void FontDownloadsAreCheckedAgainstTheirHash()
    {
        var preset = FontPresets.Find("monocraft")!;
        Assert.False(FontPresets.IsExpected(preset, Encoding.UTF8.GetBytes("not a font")));
        Assert.All(FontPresets.All, p => Assert.Matches("^[0-9a-f]{64}$", p.Sha256));
        Assert.All(FontPresets.All, p => Assert.StartsWith("https://raw.githubusercontent.com/", p.Url));
    }

    [Fact]
    public void CustomModsWithoutAFileDoNothing()
    {
        RobloxFile(ModManager.DeathSoundFile, "ouch");
        var mods = ModManager.Build(new ModSettings { DeathSound = DeathSoundChoice.Custom, Cursor = CursorChoice.Custom, UseCustomFont = true },
            new ModApplier(Version, Backups), Paths.Modifications);

        Assert.Empty(mods);
    }
}
