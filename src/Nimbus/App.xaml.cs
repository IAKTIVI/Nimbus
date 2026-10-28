using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Nimbus.Core;
using Nimbus.Installation;
using Nimbus.Roblox;
using Nimbus.UI;

namespace Nimbus;

public partial class App : Application
{
    private const string LogIdent = "App";

    private EventWaitHandle? _showSettingsSignal;

    internal static JsonStore<Settings> SettingsStore { get; private set; } = null!;
    internal static JsonStore<AppState> StateStore { get; private set; } = null!;
    internal static Settings Settings => SettingsStore.Value;

    internal static void SaveSettings()
    {
        try
        {
            SettingsStore.Save();
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var request = LaunchRequest.Parse(e.Args);
        Logger.Initialize(request.Kind.ToString());

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Logger.Error(LogIdent, $"Fatal: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) => Logger.Error(LogIdent, args.Exception);

        bool existingUser = File.Exists(Paths.SettingsFile) || File.Exists(Paths.StateFile);
        SettingsStore = new JsonStore<Settings>(Paths.SettingsFile, NimbusJson.Default.Settings);
        SettingsStore.Load();
        StateStore = new JsonStore<AppState>(Paths.StateFile, NimbusJson.Default.AppState);
        StateStore.Load();
        if (Settings.Language is null)
        {
            Settings.Language = Loc.FirstLanguage(existingUser);
            SaveSettings();
        }
        Loc.Use(Settings.Language);
        ThemeManager.Initialize(Settings);
        Installer.DeleteOldExe();
        DeleteLeftovers();

        switch (request.Kind)
        {
            case LaunchKind.Uninstall:
                Uninstall(owner: null);
                Shutdown();
                break;

            case LaunchKind.Player or LaunchKind.Link:
                new LaunchWindow(request).Begin();
                break;

            case LaunchKind.Install:
                QuietInstall();
                break;

            case LaunchKind.Update:
                UpdateInstall();
                break;

            case LaunchKind.Thanks:
                new ThanksWindow().Show();
                break;

            default:
                OpenMainUi(skipInstallCheck: request.Kind == LaunchKind.Settings);
                break;
        }
    }

    /// <summary>Files older Nimbus versions wrote that nothing uses anymore (0.7's Spotify sign-in, 0.9.0's launch card picture).</summary>
    private static void DeleteLeftovers()
    {
        try
        {
            File.Delete(Path.Combine(Paths.Base, "spotify.token"));
            File.Delete(Path.Combine(Paths.ModAssets, "LaunchIcon.png"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void OpenMainUi(bool skipInstallCheck)
    {
        bool runningInstalledCopy = Paths.IsRunningFromInstallDir;

        // A fresh download (or a newer version) that isn't installed yet: offer to install / update.
        if (!skipInstallCheck && !runningInstalledCopy && (!Installer.IsInstalled || Installer.InstalledVersion != AppInfo.Version))
        {
            new WelcomeWindow().Show();
            return;
        }

        // Files are in place but the registration is gone (e.g. a registry cleaner): quietly repair it.
        if (runningInstalledCopy && !Installer.IsInstalled)
        {
            try
            {
                Installer.Install(StateStore, Settings);
            }
            catch (Exception ex)
            {
                Logger.Error(LogIdent, ex);
            }
        }

        // Only one settings window per data folder: starting Nimbus again just brings the open one forward.
        string signalName = "Nimbus.ShowSettings." + Paths.Base.ToLowerInvariant().Replace('\\', '_');
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName, out bool createdNew);
        if (!createdNew)
        {
            _showSettingsSignal.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        ThreadPool.RegisterWaitForSingleObject(_showSettingsSignal, (_, _) => Dispatcher.BeginInvoke(window.BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);
        window.Show();
    }

    private void QuietInstall()
    {
        try
        {
            bool fresh = !Installer.IsInstalled;
            Installer.Install(StateStore, Settings);
            Installer.StartInstalled(fresh ? "-thanks" : "");
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            MessageBox.Show(Loc.T("App.InstallFailed", ex.Message), Loc.T("Welcome.InstallNimbus"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Shutdown();
    }

    /// <summary>Auto-update (started by an older Nimbus right before a launch): install without any windows.</summary>
    private void UpdateInstall()
    {
        try
        {
            Installer.Install(StateStore, Settings);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            Shutdown(1);
        }
    }

    /// <summary>Starts a fresh copy of the settings window and closes this one (e.g. after resetting settings).</summary>
    internal void Restart()
    {
        _showSettingsSignal?.Dispose(); // so the new window doesn't just hand over to this one
        _showSettingsSignal = null;
        Process.Start(new ProcessStartInfo(Paths.CurrentExe, "-settings") { UseShellExecute = false });
        Shutdown();
    }

    /// <summary>Asks for confirmation, then removes Nimbus. Returns true if it was uninstalled.</summary>
    internal static bool Uninstall(Window? owner)
    {
        string title = Loc.T("About.UninstallNimbus");

        if (IsRobloxRunning())
        {
            ShowMessage(owner, Loc.T("App.CloseRobloxFirst"), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var answer = ShowMessage(owner,
            Loc.T("App.UninstallConfirm"),
            title, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return false;

        try
        {
            Installer.Uninstall(StateStore);
            ShowMessage(owner, Loc.T("App.Uninstalled"), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            ShowMessage(owner, Loc.T("App.UninstallFailed", ex.Message), title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static bool IsRobloxRunning()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        foreach (var p in processes)
            p.Dispose();
        return processes.Length > 0;
    }

    private static MessageBoxResult ShowMessage(Window? owner, string text, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        owner is null ? MessageBox.Show(text, title, buttons, image) : MessageBox.Show(owner, text, title, buttons, image);

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error(LogIdent, e.Exception);
        MessageBox.Show(
            Loc.T("App.Crashed", e.Exception.Message, Logger.CurrentFile),
            "Nimbus", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
