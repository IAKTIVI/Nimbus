using Nimbus.Core;

namespace Nimbus.Tests;

public class PolishTests
{
    [Theory]
    [InlineData("v0.6.0", "0.5.0", true)]
    [InlineData("0.5.1", "0.5.0", true)]
    [InlineData("v1.0.0-beta", "0.9.9", true)]
    [InlineData("v0.5.0", "0.5.0", false)]
    [InlineData("v0.4.9", "0.5.0", false)]
    [InlineData("nightly", "0.5.0", false)]
    public void ReleaseVersionComparison(string tag, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(tag, current));

    [Fact]
    public void UninstallKeepsTheUsersSettings()
    {
        string command = Nimbus.Installation.Installer.RemovalCommand();
        Assert.Contains(Paths.InstalledExe, command);
        Assert.Contains(Paths.Versions, command);
        Assert.DoesNotContain(Paths.SettingsFile, command);
        Assert.DoesNotContain(Paths.ModAssets + "\"", command);
        Assert.DoesNotContain("\"" + Paths.Base + "\"", command);
    }

    [Fact]
    public void ResetGoesBackToDefaults()
    {
        string file = Path.Combine(Directory.CreateTempSubdirectory("NimbusReset").FullName, "Settings.json");
        var store = new JsonStore<Settings>(file, NimbusJson.Default.Settings);
        store.Load();
        store.Value.AccentColor = "#FF375F";
        store.Value.LaunchStyle = LaunchStyle.HiddenUnlessUpdating;
        store.Save();

        store.Reset();
        var reloaded = new JsonStore<Settings>(file, NimbusJson.Default.Settings).Load();

        Assert.Equal("#0A84FF", reloaded.AccentColor);
        Assert.Equal(LaunchStyle.GlassCard, reloaded.LaunchStyle);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }
}
