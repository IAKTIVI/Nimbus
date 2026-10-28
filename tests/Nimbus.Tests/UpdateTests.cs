using System.Text.Json;
using Nimbus.Core;
using Nimbus.Roblox;

namespace Nimbus.Tests;

public class UpdateTests
{
    [Fact]
    public void AutoUpdateIsOnByDefault() => Assert.True(new Settings().AutoUpdate);

    [Fact]
    public void OldSettingsFilesGetAutoUpdateOn()
    {
        string file = Path.Combine(Directory.CreateTempSubdirectory("NimbusAutoUpdate").FullName, "Settings.json");
        File.WriteAllText(file, """{ "AccentColor": "#FF375F" }""");

        var settings = new JsonStore<Settings>(file, NimbusJson.Default.Settings).Load();

        Assert.True(settings.AutoUpdate);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }

    [Fact]
    public void FindsTheExeInARelease()
    {
        using var doc = JsonDocument.Parse("""
            { "tag_name": "v0.9.0", "assets": [
                { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt" },
                { "name": "Nimbus.exe", "browser_download_url": "https://example.com/Nimbus.exe" } ] }
            """);
        Assert.Equal("https://example.com/Nimbus.exe", UpdateChecker.FindExeAsset(doc.RootElement));
    }

    [Fact]
    public void ReleaseWithoutExeHasNoDownload()
    {
        using var doc = JsonDocument.Parse("""{ "tag_name": "v0.9.0", "assets": [] }""");
        Assert.Null(UpdateChecker.FindExeAsset(doc.RootElement));
        using var bare = JsonDocument.Parse("""{ "tag_name": "v0.9.0" }""");
        Assert.Null(UpdateChecker.FindExeAsset(bare.RootElement));
    }

    [Fact]
    public void OnlyRealExesAreInstalled()
    {
        string dir = Directory.CreateTempSubdirectory("NimbusExeCheck").FullName;
        string exe = Path.Combine(dir, "good.exe");
        string page = Path.Combine(dir, "page.exe");
        string tiny = Path.Combine(dir, "tiny.exe");
        byte[] body = new byte[200 * 1024];
        body[0] = (byte)'M';
        body[1] = (byte)'Z';
        File.WriteAllBytes(exe, body);
        File.WriteAllText(page, "<html>" + new string(' ', 200 * 1024));
        File.WriteAllBytes(tiny, [(byte)'M', (byte)'Z', 0, 0]);

        Assert.True(SelfUpdater.LooksLikeExe(exe));
        Assert.False(SelfUpdater.LooksLikeExe(page));
        Assert.False(SelfUpdater.LooksLikeExe(tiny));
        Assert.False(SelfUpdater.LooksLikeExe(Path.Combine(dir, "missing.exe")));
        Directory.Delete(dir, recursive: true);
    }

    [Theory]
    [InlineData("-update", LaunchKind.Update)]
    [InlineData("-thanks", LaunchKind.Thanks)]
    public void ParsesNewSwitches(string arg, LaunchKind kind) =>
        Assert.Equal(kind, LaunchRequest.Parse([arg]).Kind);

    [Fact]
    public void HandOverRepeatsTheSameLaunch()
    {
        Assert.Equal("-player", LaunchRequest.Parse(["-player"]).ToArguments());

        var link = LaunchRequest.Parse(["roblox://placeId=1818"]);
        Assert.Equal("\"roblox://placeId=1818\"", link.ToArguments());
        Assert.Equal(link, LaunchRequest.Parse([link.ToArguments()]));
    }
}
