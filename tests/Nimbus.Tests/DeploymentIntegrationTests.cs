using Nimbus.Core;
using Nimbus.Roblox;

namespace Nimbus.Tests;

/// <summary>Runs only when NIMBUS_INTEGRATION=1, because it downloads the real Roblox client (~225 MB).</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NIMBUS_INTEGRATION") != "1")
            Skip = "Set NIMBUS_INTEGRATION=1 to run tests that download Roblox";
    }
}

public class DeploymentIntegrationTests
{
    /// <summary>NIMBUS_INTEGRATION_DIR if set, so repeated runs reuse the download cache.</summary>
    private static string Root =>
        Environment.GetEnvironmentVariable("NIMBUS_INTEGRATION_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Path.GetTempPath(), "NimbusIntegration");

    [IntegrationFact]
    public async Task InstallsTheLatestRobloxPlayer()
    {
        var deployment = new RobloxDeployment(Path.Combine(Root, "Versions"), Path.Combine(Root, "Downloads"), Http.Client);

        var latest = await deployment.GetLatestVersionAsync(CancellationToken.None);
        Assert.True(RobloxDeployment.IsValidVersionHash(latest.ClientVersionUpload));

        var reports = new List<DeployProgress>();
        string dir = await deployment.InstallAsync(latest.ClientVersionUpload, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.True(deployment.IsInstalled(latest.ClientVersionUpload));
        Assert.True(File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe")));
        Assert.True(File.Exists(Path.Combine(dir, "AppSettings.xml")));
        Assert.True(Directory.Exists(Path.Combine(dir, "content", "textures")));
        Assert.True(Directory.Exists(Path.Combine(dir, "PlatformContent", "pc", "terrain")));
        Assert.Contains(reports, r => r.Fraction is > 0 and < 1);
        Assert.Equal(1, reports[^1].Fraction);
    }

    [IntegrationFact]
    public async Task UnknownVersionGivesAClearError()
    {
        var deployment = new RobloxDeployment(Path.Combine(Root, "Versions"), Path.Combine(Root, "Downloads"), Http.Client);
        await Assert.ThrowsAsync<FileNotFoundException>(() => deployment.GetManifestAsync("version-0000000000000000", CancellationToken.None));
    }

    private sealed class SyncProgress(Action<DeployProgress> report) : IProgress<DeployProgress>
    {
        public void Report(DeployProgress value)
        {
            lock (this)
                report(value);
        }
    }
}
