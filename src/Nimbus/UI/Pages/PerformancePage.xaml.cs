using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Nimbus.Core;
using Nimbus.Performance;
using Nimbus.UI.Controls;

namespace Nimbus.UI.Pages;

public partial class PerformancePage : UserControl, IPage
{
    private const string LogIdent = "PerformancePage";

    private static (CleanupKind Kind, string Title, string Description)[] CleanupItems =>
    [
        (CleanupKind.Logs, Loc.T("Performance.CleanLogs"), Loc.T("Performance.CleanLogsDesc")),
        (CleanupKind.TempFiles, Loc.T("Performance.CleanTemp"), Loc.T("Performance.CleanTempDesc")),
        (CleanupKind.InstallerDownloads, Loc.T("Performance.CleanInstaller"), Loc.T("Performance.CleanInstallerDesc")),
        (CleanupKind.AssetCache, Loc.T("Performance.CleanCache"), Loc.T("Performance.CleanCacheDesc")),
        (CleanupKind.NimbusDownloads, Loc.T("Performance.CleanNimbus"), Loc.T("Performance.CleanNimbusDesc")),
    ];

    private readonly Dictionary<CleanupKind, (TextBlock Size, Button Clean)> _cleanupRows = [];
    private bool _loading;

    public PerformancePage()
    {
        InitializeComponent();
        BuildCleanupRows();
        OnShown();
    }

    private static PerformanceSettings Performance => App.Settings.Performance;

    public void OnShown()
    {
        _loading = true;
        PriorityPicker.SelectedIndex = (int)Performance.Priority;
        FpsPicker.SelectedIndex = Performance.FramerateCap is { } cap ? Array.IndexOf(FramerateCap.Choices, cap) + 1 : 0;
        GpuToggle.IsChecked = Performance.HighPerformanceGpu;
        FsoToggle.IsChecked = Performance.DisableFullscreenOptimizations;
        CleanOnLaunchToggle.IsChecked = Performance.CleanOnLaunch;
        _loading = false;

        RefreshFpsDescription();
        _ = MeasureAsync();
    }

    private void RefreshFpsDescription()
    {
        string text = Loc.T("Performance.FpsInfo");
        try
        {
            if (FramerateCap.Read(FramerateCap.DefaultFile) is { } current)
                text += " " + (current > 0 ? Loc.T("Performance.FpsNow", current) : Loc.T("Performance.FpsNoLimit"));
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, ex.Message);
        }

        FpsRow.Description = text;
    }

    private void Picker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        Performance.Priority = (RobloxPriority)Math.Max(0, PriorityPicker.SelectedIndex);
        Performance.FramerateCap = FpsPicker.SelectedIndex > 0 ? FramerateCap.Choices[FpsPicker.SelectedIndex - 1] : null;
        App.SaveSettings();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        Performance.HighPerformanceGpu = GpuToggle.IsChecked == true;
        Performance.DisableFullscreenOptimizations = FsoToggle.IsChecked == true;
        Performance.CleanOnLaunch = CleanOnLaunchToggle.IsChecked == true;
        App.SaveSettings();
    }

    // ---------- Clean up ----------

    private void BuildCleanupRows()
    {
        for (int i = 0; i < CleanupItems.Length; i++)
        {
            var (kind, title, description) = CleanupItems[i];
            if (i > 0)
                CleanupList.Children.Add(new Border { Style = (Style)FindResource("RowSeparator") });

            var size = new TextBlock { Text = "…", Style = (Style)FindResource("ValueText"), Margin = new Thickness(0, 0, 14, 0), MinWidth = 60 };
            var clean = new Button { Content = Loc.T("Performance.Clean"), IsEnabled = false };
            clean.Click += async (_, _) => await CleanAsync(kind, title);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(size);
            right.Children.Add(clean);

            if (Cleaner.NeedsRobloxClosed(kind))
                description += " " + Loc.T("Performance.CloseRobloxFirst");

            CleanupList.Children.Add(new SettingRow { Title = title, Description = description, Content = right });
            _cleanupRows[kind] = (size, clean);
        }
    }

    /// <summary>Folder sizes are measured in the background: the asset cache alone can be thousands of files.</summary>
    private async Task MeasureAsync()
    {
        foreach (var (kind, (size, clean)) in _cleanupRows)
        {
            long bytes = await Task.Run(() =>
            {
                try { return Cleaner.Measure(kind); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0L; }
            });

            size.Text = bytes == 0 ? Loc.T("Performance.Empty") : FormatSize(bytes);
            clean.IsEnabled = bytes > 0;
        }
    }

    private async Task CleanAsync(CleanupKind kind, string title)
    {
        var (_, clean) = _cleanupRows[kind];
        clean.IsEnabled = false;
        CleanStatus.Visibility = Visibility.Visible;
        CleanStatus.Text = Loc.T("Performance.Cleaning", title);

        try
        {
            var result = await Task.Run(() => Cleaner.Clean(kind));
            CleanStatus.Text = Loc.T("Performance.Freed", title, FormatSize(result.FreedBytes)) +
                (result.SkippedFiles > 0 ? " " + Loc.T("Performance.Skipped", result.SkippedFiles) : "");
        }
        catch (InvalidOperationException ex)
        {
            CleanStatus.Text = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            CleanStatus.Text = Loc.T("Performance.CleanFailed", title, ex.Message);
        }

        await MeasureAsync();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB",
        _ => Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB",
    };
}
