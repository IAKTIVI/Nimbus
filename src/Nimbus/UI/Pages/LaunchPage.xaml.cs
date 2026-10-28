using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Nimbus.Core;
using Nimbus.Installation;
using Nimbus.Roblox;

namespace Nimbus.UI.Pages;

public partial class LaunchPage : UserControl
{
    private const string LogIdent = "LaunchPage";

    private readonly RobloxDeployment _deployment = RobloxDeployment.CreateDefault();
    private bool _busy;

    public LaunchPage()
    {
        InitializeComponent();

        MultiInstanceToggle.IsChecked = App.Settings.MultiInstance;
        DesktopToggle.IsChecked = App.Settings.DesktopShortcut;
        PinToggle.IsChecked = App.Settings.PinnedVersion is not null;
        PinHashBox.Text = App.Settings.PinnedVersion ?? "";
        UpdatePinRow();

        Loaded += async (_, _) =>
        {
            RefreshInstalled();
            RefreshProtocol();
            await CheckForUpdatesAsync();
        };
    }

    private string? InstalledVersion =>
        App.StateStore.Value.InstalledVersion is { } v && RobloxDeployment.IsValidVersionHash(v) && _deployment.IsInstalled(v) ? v : null;

    private void RefreshInstalled()
    {
        var state = App.StateStore.Value;
        InstalledText.Text = InstalledVersion is null
            ? Loc.T("LaunchPage.NotInstalled")
            : state.InstalledVersionNumber ?? state.InstalledVersion;
    }

    private void RefreshProtocol()
    {
        bool installed = Installer.IsInstalled;
        bool registered = installed && ProtocolHandler.IsRegistered(Paths.InstalledExe);

        ProtocolRow.IsEnabled = installed;
        DesktopRow.IsEnabled = installed;
        ProtocolRow.Description = installed
            ? Loc.T("Launch.OpenRobloxLinksWithDesc")
            : Loc.T("LaunchPage.InstallFirst");
        ProtocolOnText.Visibility = registered ? Visibility.Visible : Visibility.Collapsed;
        ProtocolButton.Visibility = registered ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_busy)
            return;

        UpdateButton.Visibility = Visibility.Collapsed;

        if (RobloxDeployment.IsValidVersionHash(App.Settings.PinnedVersion))
        {
            bool pinnedInstalled = string.Equals(InstalledVersion, App.Settings.PinnedVersion, StringComparison.OrdinalIgnoreCase);
            UpdateRow.Description = pinnedInstalled
                ? Loc.T("LaunchPage.StayingPinned")
                : Loc.T("LaunchPage.PinnedNextLaunch");
            ShowUpdateButton(!pinnedInstalled, Loc.T("LaunchPage.InstallItNow"));
            return;
        }

        UpdateRow.Description = Loc.T("LaunchPage.Checking");
        CheckButton.IsEnabled = false;
        try
        {
            var latest = await _deployment.GetLatestVersionAsync(CancellationToken.None);
            string? installed = InstalledVersion;

            if (installed is null)
            {
                UpdateRow.Description = Loc.T("LaunchPage.NotInstalledLatest", latest.Version);
                ShowUpdateButton(true, Loc.T("LaunchPage.InstallNow"));
            }
            else if (string.Equals(installed, latest.ClientVersionUpload, StringComparison.OrdinalIgnoreCase))
            {
                UpdateRow.Description = Loc.T("LaunchPage.UpToDate");
            }
            else
            {
                UpdateRow.Description = Loc.T("LaunchPage.UpdateAvailable", latest.Version);
                ShowUpdateButton(true, Loc.T("Launch.UpdateNow"));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Update check failed: {ex.Message}");
            UpdateRow.Description = Loc.T("LaunchPage.CheckFailed");
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private void ShowUpdateButton(bool show, string text)
    {
        UpdateButton.Content = text;
        UpdateButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        _busy = true;
        CheckButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        UpdateProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<DeployProgress>(p =>
        {
            UpdateStatus.Text = p.Status;
            UpdateProgress.IsIndeterminate = p.Fraction is null;
            if (p.Fraction is { } fraction)
                UpdateProgress.Value = fraction;
        });

        try
        {
            var bootstrapper = new Bootstrapper(App.Settings, App.StateStore, _deployment);
            await bootstrapper.EnsureRobloxAsync(progress, CancellationToken.None);
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Value = 0;
            UpdateStatus.Text = Loc.T("LaunchPage.UpdateFailed", ex.Message);
        }
        finally
        {
            _busy = false;
            CheckButton.IsEnabled = true;
            UpdateButton.IsEnabled = true;
        }

        RefreshInstalled();
        await CheckForUpdatesAsync();
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        var running = Process.GetProcessesByName("RobloxPlayerBeta");
        foreach (var p in running)
            p.Dispose();

        UpdateProgressPanel.Visibility = Visibility.Visible;
        if (running.Length > 0)
        {
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Value = 0;
            UpdateStatus.Text = Loc.T("LaunchPage.CloseForRepair");
            return;
        }

        try
        {
            if (App.StateStore.Value.InstalledVersion is { } hash && RobloxDeployment.IsValidVersionHash(hash))
            {
                _deployment.Remove(hash);
                string backups = Path.Combine(Paths.ModBackups, hash);
                if (Directory.Exists(backups))
                    Directory.Delete(backups, recursive: true);
            }

            App.StateStore.Value.InstalledVersion = null;
            App.StateStore.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error(LogIdent, ex);
            UpdateStatus.Text = Loc.T("LaunchPage.RepairFailed", ex.Message);
            return;
        }

        RefreshInstalled();
        Update_Click(sender, e); // download it again
    }

    private void Launch_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Paths.CurrentExe, "-player") { UseShellExecute = false });

    private void Protocol_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProtocolHandler.Register(Paths.InstalledExe, App.StateStore.Value);
            App.StateStore.Save();
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
        }

        RefreshProtocol();
    }

    private void MultiInstance_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.MultiInstance = MultiInstanceToggle.IsChecked == true;
        App.SaveSettings();
    }

    private void Desktop_Click(object sender, RoutedEventArgs e)
    {
        bool show = DesktopToggle.IsChecked == true;
        try
        {
            Installer.SetDesktopShortcut(show);
            App.Settings.DesktopShortcut = show;
            App.SaveSettings();
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            DesktopToggle.IsChecked = !show;
        }
    }

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (PinToggle.IsChecked == true && string.IsNullOrWhiteSpace(PinHashBox.Text))
            PinHashBox.Text = InstalledVersion ?? ""; // the sensible default: stay on what's installed now

        SavePin();
        UpdatePinRow();
        await CheckForUpdatesAsync();
    }

    private void PinHash_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        SavePin();
        UpdatePinRow();
    }

    private void SavePin()
    {
        string text = PinHashBox.Text.Trim();
        bool valid = RobloxDeployment.IsValidVersionHash(text);

        string? pinned = PinToggle.IsChecked == true && valid ? text.ToLowerInvariant() : null;
        if (PinToggle.IsChecked == true && !valid)
            pinned = App.Settings.PinnedVersion; // keep the last good value until the text is fixed

        if (pinned != App.Settings.PinnedVersion)
        {
            App.Settings.PinnedVersion = pinned;
            App.SaveSettings();
        }
    }

    private void UpdatePinRow()
    {
        bool on = PinToggle.IsChecked == true;
        PinHashRow.IsEnabled = on;
        bool invalid = on && PinHashBox.Text.Trim().Length > 0 && !RobloxDeployment.IsValidVersionHash(PinHashBox.Text.Trim());
        PinError.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
    }
}
