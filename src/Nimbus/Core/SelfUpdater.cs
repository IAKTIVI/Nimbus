using System.Diagnostics;

namespace Nimbus.Core;

/// <summary>
/// Auto-update: right before Roblox starts, downloads a newer Nimbus.exe from GitHub and installs it quietly.
/// The caller then hands the launch over to the new copy.
/// </summary>
internal static class SelfUpdater
{
    private const string LogIdent = "SelfUpdater";

    /// <summary>Set for the copy started after an update, so it doesn't check again straight away.</summary>
    private const string JustUpdatedVariable = "NIMBUS_JUST_UPDATED";

    /// <summary>A launch shouldn't wait long on GitHub; if it's slow, update next time.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Anything smaller can't be a real Nimbus build (it's ~600 KB).</summary>
    private const long MinimumExeSize = 100 * 1024;

    /// <summary>
    /// True when this launch should look for an update: switched on, running the installed single-file EXE,
    /// not just updated, and no Roblox open (a running game is never updated under).
    /// </summary>
    public static bool ShouldCheck(Settings settings) =>
        settings.AutoUpdate
        && UpdateChecker.IsConfigured
        && Paths.IsSingleFile
        && Paths.IsRunningFromInstallDir
        && Environment.GetEnvironmentVariable(JustUpdatedVariable) is null
        && !IsRobloxRunning();

    /// <summary>
    /// Installs a newer Nimbus if there is one. Returns true when it did (the installed EXE is now the new
    /// version); false when there's nothing to do or anything went wrong, in which case the launch just goes on.
    /// </summary>
    public static async Task<bool> TryUpdateAsync(IProgress<(string Status, double? Fraction)> progress, CancellationToken ct)
    {
        NimbusRelease? release;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(CheckTimeout);
            release = await UpdateChecker.CheckAsync(timeout.Token);
        }

        ct.ThrowIfCancellationRequested();
        if (release?.DownloadUrl is null)
            return false;

        string download = Path.Combine(Paths.Downloads, $"Nimbus-{release.Version}.exe");
        try
        {
            Logger.Info(LogIdent, $"Updating Nimbus {AppInfo.Version} → {release.Version}");
            progress.Report((Loc.T("Update.Downloading", release.Version), 0));
            Directory.CreateDirectory(Paths.Downloads);
            await DownloadAsync(release.DownloadUrl, download, f => progress.Report((Loc.T("Update.Downloading", release.Version), f)), ct);

            if (!LooksLikeExe(download))
            {
                Logger.Warn(LogIdent, "The downloaded file isn't a Nimbus EXE; skipping the update");
                return false;
            }

            progress.Report((Loc.T("Update.Installing"), null));
            using var installer = Process.Start(new ProcessStartInfo(download, "-update") { UseShellExecute = false, WorkingDirectory = Paths.Downloads })!;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                wait.CancelAfter(TimeSpan.FromSeconds(60));
                await installer.WaitForExitAsync(wait.Token);
            }

            if (installer.ExitCode != 0)
            {
                Logger.Warn(LogIdent, $"The update installer exited with {installer.ExitCode}");
                return false;
            }

            Logger.Info(LogIdent, $"Nimbus {release.Version} installed");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Auto-update failed, launching anyway: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(download); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Starts the freshly installed Nimbus with the same launch, so the new version takes over.</summary>
    public static void HandOver(string arguments)
    {
        var start = new ProcessStartInfo(Paths.InstalledExe, arguments) { UseShellExecute = false, WorkingDirectory = Paths.Base };
        start.Environment[JustUpdatedVariable] = "1";
        Process.Start(start)?.Dispose();
    }

    /// <summary>A Windows program starts with "MZ"; an HTML error page or a cut-off download doesn't pass.</summary>
    internal static bool LooksLikeExe(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < MinimumExeSize)
            return false;

        using var stream = info.OpenRead();
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    private static async Task DownloadAsync(string url, string target, Action<double> onProgress, CancellationToken ct)
    {
        using var response = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(target);
        byte[] buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
                onProgress((double)done / total.Value);
        }
    }

    private static bool IsRobloxRunning()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        foreach (var p in processes)
            p.Dispose();
        return processes.Length > 0;
    }
}
