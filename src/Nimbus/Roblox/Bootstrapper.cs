using Nimbus.Core;
using Nimbus.Mods;
using Nimbus.Performance;

namespace Nimbus.Roblox;

/// <summary>Decides which Roblox version should run, installs it if needed, and keeps State.json in sync.</summary>
internal sealed class Bootstrapper(Settings settings, JsonStore<AppState> state, RobloxDeployment deployment)
{
    private const string LogIdent = "Bootstrapper";

    /// <summary>Makes sure the right Roblox version is installed and returns its folder.</summary>
    public async Task<string> EnsureRobloxAsync(IProgress<DeployProgress> progress, CancellationToken ct)
    {
        // Two launches at once (e.g. multi-instance) must not install into the same folder together.
        await using var _ = await AcquireDeployLockAsync(progress, ct);

        string? installed = state.Value.InstalledVersion;
        bool haveInstalled = RobloxDeployment.IsValidVersionHash(installed) && deployment.IsInstalled(installed!);

        string target;
        string? versionNumber = null;

        if (RobloxDeployment.IsValidVersionHash(settings.PinnedVersion))
        {
            target = settings.PinnedVersion!.ToLowerInvariant();
            Logger.Info(LogIdent, $"Version pinned to {target}");
        }
        else
        {
            progress.Report(new DeployProgress(Loc.T("Deploy.Checking"), null));
            try
            {
                var latest = await deployment.GetLatestVersionAsync(ct);
                target = latest.ClientVersionUpload.ToLowerInvariant();
                versionNumber = latest.Version;
            }
            catch (Exception ex) when (haveInstalled && !ct.IsCancellationRequested)
            {
                // Offline or Roblox is down: launching the version we already have beats not launching at all.
                Logger.Warn(LogIdent, $"Update check failed, launching installed {installed}: {ex.Message}");
                return ApplyCustomizations(deployment.GetVersionDirectory(installed!));
            }
        }

        if (!deployment.IsInstalled(target))
            await deployment.InstallAsync(target, progress, ct);

        if (!string.Equals(state.Value.InstalledVersion, target, StringComparison.OrdinalIgnoreCase))
        {
            state.Value.InstalledVersion = target;
            state.Value.InstalledVersionNumber = versionNumber;
            state.Save();
        }
        else if (versionNumber is not null && versionNumber != state.Value.InstalledVersionNumber)
        {
            state.Value.InstalledVersionNumber = versionNumber;
            state.Save();
        }

        deployment.CleanupOldVersions(target);
        ModManager.CleanupBackups(target);
        return ApplyCustomizations(deployment.GetVersionDirectory(target));
    }

    /// <summary>Re-applied on every launch, so a fresh Roblox update never loses the user's tweaks.</summary>
    private string ApplyCustomizations(string versionDirectory)
    {
        FastFlags.Apply(versionDirectory, settings);
        ModManager.Apply(versionDirectory, settings);
        ApplyPerformance(versionDirectory);
        return versionDirectory;
    }

    /// <summary>Everything on the Performance page that has to happen before Roblox starts. Never blocks the launch.</summary>
    private void ApplyPerformance(string versionDirectory)
    {
        var performance = settings.Performance;
        try
        {
            if (performance.CleanOnLaunch)
                Cleaner.CleanBeforeLaunch();

            // Roblox saves its settings file when it closes, so writing while it runs would just be overwritten.
            if (performance.FramerateCap is { } cap && !Cleaner.IsRobloxOrStudioRunning())
                FramerateCap.Write(FramerateCap.DefaultFile, cap);

            var before = state.Value.TweakedExePaths.ToList();
            WindowsTweaks.Apply(Path.Combine(versionDirectory, RobloxDeployment.PlayerExe), performance, state.Value);
            if (!before.SequenceEqual(state.Value.TweakedExePaths))
                state.Save();
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
        }
    }

    /// <summary>An exclusive lock file; Windows releases it automatically if Nimbus crashes.</summary>
    private static async Task<FileStream> AcquireDeployLockAsync(IProgress<DeployProgress> progress, CancellationToken ct)
    {
        string path = Path.Combine(Paths.Base, "deploy.lock");
        Directory.CreateDirectory(Paths.Base);

        bool waiting = false;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                if (!waiting)
                {
                    progress.Report(new DeployProgress(Loc.T("Deploy.Waiting"), null));
                    waiting = true;
                }

                await Task.Delay(500, ct);
            }
        }
    }
}
