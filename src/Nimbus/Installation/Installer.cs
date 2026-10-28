using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Nimbus.Core;

namespace Nimbus.Installation;

/// <summary>Per-user install: no admin rights, everything under %LocalAppData%\Nimbus.</summary>
internal static partial class Installer
{
    private const string LogIdent = "Installer";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Nimbus";

    /// <summary>
    /// When Nimbus is started from inside a packaged (Store / MSIX) app's sandbox, Windows silently redirects
    /// AppData writes into that app's private Packages\...\LocalCache folder, so an install would be invisible to
    /// everything else (the Start menu, the website's Play button, Nimbus started normally). This writes a probe
    /// file and asks Windows where it really landed. Returns that private folder, or null when all is normal.
    /// </summary>
    public static string? FindSandboxRedirect()
    {
        bool existed = Directory.Exists(Paths.Base);
        Directory.CreateDirectory(Paths.Base);
        string probe = Path.Combine(Paths.Base, $".probe-{Environment.ProcessId}");
        try
        {
            File.WriteAllText(probe, "");
            using var handle = File.OpenHandle(probe);

            char[] buffer = new char[1024];
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length)
                return null;

            string real = Path.GetDirectoryName(new string(buffer, 0, (int)length).Replace(@"\\?\", ""))!;
            bool redirected = real.Contains(@"\AppData\Local\Packages\", StringComparison.OrdinalIgnoreCase)
                && real.Contains(@"\LocalCache\", StringComparison.OrdinalIgnoreCase);
            return redirected ? real : null;
        }
        finally
        {
            try
            {
                File.Delete(probe);
                if (!existed)
                    Directory.Delete(Paths.Base);
            }
            catch (IOException)
            {
                // Logs may already be in there; leaving an empty folder is harmless.
            }
        }
    }

    private static string SettingsShortcut => Path.Combine(Paths.StartMenuPrograms, "Nimbus.lnk");
    private static string PlayerShortcut => Path.Combine(Paths.StartMenuPrograms, "Roblox Player (Nimbus).lnk");
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Roblox (Nimbus).lnk");

    /// <summary>Adds or removes the "Roblox (Nimbus)" desktop icon, which opens the Roblox app through Nimbus.</summary>
    public static void SetDesktopShortcut(bool show)
    {
        if (show)
            CreateShortcut(DesktopShortcut, Paths.InstalledExe, "-player", "Play Roblox through Nimbus");
        else if (File.Exists(DesktopShortcut))
            File.Delete(DesktopShortcut);
    }

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key is not null && File.Exists(Paths.InstalledExe);
        }
    }

    public static string? InstalledVersion
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("DisplayVersion") as string;
        }
    }

    public static void Install(JsonStore<AppState> state, Settings settings)
    {
        if (FindSandboxRedirect() is { } privateFolder)
        {
            Logger.Error(LogIdent, $"Install blocked: AppData writes are redirected to {privateFolder}");
            throw new InvalidOperationException(
                Loc.T("Installer.Sandboxed"));
        }

        Logger.Info(LogIdent, $"Installing Nimbus {AppInfo.Version} to {Paths.Base}");
        Directory.CreateDirectory(Paths.Base);

        if (!Paths.IsRunningFromInstallDir)
            CopySelf();

        ProtocolHandler.Register(Paths.InstalledExe, state.Value);
        state.Save();

        CreateShortcut(SettingsShortcut, Paths.InstalledExe, "", "Nimbus settings");
        CreateShortcut(PlayerShortcut, Paths.InstalledExe, "-player", "Launch Roblox through Nimbus");
        if (settings.DesktopShortcut)
            SetDesktopShortcut(true);
        WriteUninstallEntry();
        Logger.Info(LogIdent, "Install complete");
    }

    public static void Uninstall(JsonStore<AppState> state)
    {
        Logger.Info(LogIdent, "Uninstalling Nimbus");
        ProtocolHandler.Unregister(state.Value);
        Performance.WindowsTweaks.RemoveAll(state.Value);
        state.Save();

        foreach (string shortcut in new[] { SettingsShortcut, PlayerShortcut, DesktopShortcut })
        {
            try { File.Delete(shortcut); }
            catch (IOException) { /* already gone */ }
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        // A running EXE can't delete itself, so a hidden cmd finishes the job once Nimbus has exited.
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = RemovalCommand(),
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    /// <summary>
    /// Removes Nimbus itself, the Roblox copy and other leftovers, but keeps what the user set up (settings, profiles,
    /// widgets, history, playtime, their mods, sounds, fonts and cursor), so a reinstall picks it all back up.
    /// </summary>
    internal static string RemovalCommand()
    {
        string[] files = [Paths.InstalledExe, OldExe, Paths.StateFile, Path.Combine(Paths.Base, "deploy.lock"), Path.Combine(Paths.Base, "spotify.token")];
        string[] folders = [Paths.Versions, Paths.Downloads, Paths.Logs, Paths.ModBackups];
        return "/c ping 127.0.0.1 -n 3 > nul" +
            " & del /f /q " + string.Join(" ", files.Select(f => $"\"{f}\"")) +
            " & rmdir /s /q " + string.Join(" ", folders.Select(f => $"\"{f}\""));
    }

    private static string OldExe => Paths.InstalledExe + ".old";

    /// <summary>Deletes the EXE an update moved aside, once nothing runs from it anymore.</summary>
    public static void DeleteOldExe()
    {
        try
        {
            if (File.Exists(OldExe))
                File.Delete(OldExe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // still running; next time
        }
    }

    /// <summary>Starts the installed copy of Nimbus (used right after installing).</summary>
    public static void StartInstalled(string arguments = "") =>
        Process.Start(new ProcessStartInfo(Paths.InstalledExe, arguments) { UseShellExecute = false, WorkingDirectory = Paths.Base });

    private static void CopySelf()
    {
        if (Paths.IsSingleFile)
        {
            try
            {
                File.Copy(Paths.CurrentExe, Paths.InstalledExe, overwrite: true);
            }
            catch (IOException) when (File.Exists(Paths.InstalledExe))
            {
                // A running Nimbus (e.g. the tray helper during a game) keeps its EXE open, but Windows still lets
                // it be renamed. The old copy keeps running from the new name and is deleted on a later start.
                File.Move(Paths.InstalledExe, OldExe, overwrite: true);
                File.Copy(Paths.CurrentExe, Paths.InstalledExe);
                Logger.Info(LogIdent, "Nimbus was running; its old EXE was moved aside");
            }

            return;
        }

        // Dev build: Nimbus.exe needs the DLLs next to it, so copy the whole output folder.
        string source = AppContext.BaseDirectory;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(Paths.Base, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WriteUninstallEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppInfo.Name);
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("DisplayIcon", $"\"{Paths.InstalledExe}\",0");
        key.SetValue("Publisher", AppInfo.Name);
        key.SetValue("InstallLocation", Paths.Base);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", $"\"{Paths.InstalledExe}\" -uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] buffer, uint length, uint flags);

    private static void CreateShortcut(string path, string target, string arguments, string description)
    {
        // WScript.Shell is the simplest way to write a .lnk without a big interop layer.
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            try
            {
                link.TargetPath = target;
                link.Arguments = arguments;
                link.WorkingDirectory = Path.GetDirectoryName(target);
                link.IconLocation = $"{target},0";
                link.Description = description;
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
