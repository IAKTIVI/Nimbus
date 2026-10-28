using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Nimbus.Core;

namespace Nimbus.UI.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = Loc.T("About.Version", AppInfo.Version);
        FolderRow.Description = Paths.Base;
        AutoUpdateToggle.IsChecked = App.Settings.AutoUpdate;
        Loaded += async (_, _) => await CheckForUpdateAsync();
    }

    private string? _releaseUrl;

    private async Task CheckForUpdateAsync()
    {
        if (!UpdateChecker.IsConfigured)
        {
            UpdateRow.Description = Loc.T("About.NoRepository");
            return;
        }

        var release = await UpdateChecker.CheckAsync();
        _releaseUrl = release?.Url;
        UpdateRow.Description = release is null
            ? Loc.T("About.Latest", AppInfo.Version)
            : App.Settings.AutoUpdate && release.DownloadUrl is not null
                ? Loc.T("About.AvailableAuto", release.Version)
                : Loc.T("About.Available", release.Version);
        DownloadButton.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_releaseUrl is not null)
            Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
    }

    private void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.AutoUpdate = AutoUpdateToggle.IsChecked == true;
        App.SaveSettings();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            Loc.T("About.ResetConfirm"),
            Loc.T("About.ResetAllSettings"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        App.SettingsStore.Reset();
        ((App)Application.Current).Restart();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenInExplorer(Paths.Base);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenInExplorer(Paths.Logs);

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (App.Uninstall(Window.GetWindow(this)))
            Application.Current.Shutdown();
    }

    private static void OpenInExplorer(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
    }
}
