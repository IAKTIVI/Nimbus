using System.Diagnostics;
using System.Windows;
using Nimbus.Core;
using Nimbus.Installation;
using Nimbus.Integrations;
using Nimbus.Roblox;

namespace Nimbus.UI;

/// <summary>The small glass card shown while Nimbus updates and starts Roblox.</summary>
public partial class LaunchWindow : Window
{
    private const string LogIdent = "LaunchWindow";

    private readonly LaunchRequest? _request;
    private CancellationTokenSource _cts = new();
    private bool _updateChecked;

    /// <param name="request">The launch to run; null for the Appearance page's preview, which starts nothing.</param>
    internal LaunchWindow(LaunchRequest? request)
    {
        _request = request;
        InitializeComponent();
        Glass.Attach(this);
    }

    /// <summary>Shows the card for a few seconds without starting Roblox (Appearance → Preview).</summary>
    public async void Preview()
    {
        ApplyStyle(App.Settings.LaunchStyle == LaunchStyle.Minimal ? LaunchStyle.Minimal : LaunchStyle.GlassCard);
        ShowWorking(Loc.T("Card.StartingRoblox"));
        Show();
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (IsLoaded) // not closed with Cancel already
            Close();
    }

    /// <summary>Starts the launch, showing the window in the style the user picked on the Appearance page.</summary>
    public void Begin()
    {
        var style = App.Settings.LaunchStyle;
        ApplyStyle(style);

        if (style != LaunchStyle.HiddenUnlessUpdating)
            Show();

        _ = RunAsync();
    }

    private void ApplyStyle(LaunchStyle style)
    {
        if (style == LaunchStyle.Minimal)
        {
            // Just the status line and progress bar.
            Logo.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Collapsed;
            Body.Margin = new Thickness(0);
            Card.Margin = new Thickness(18, 14, 18, 14);
            Width = 340;
        }
    }

    private async Task RunAsync()
    {
        if (_request is null)
            return;

        ShowWorking(Loc.T("Card.Starting"));
        try
        {
            // Roblox's own installer likes to take the links back; quietly reclaim them on every launch.
            if (Paths.IsRunningFromInstallDir && !ProtocolHandler.IsRegistered(Paths.InstalledExe))
            {
                ProtocolHandler.Register(Paths.InstalledExe, App.StateStore.Value);
                App.StateStore.Save();
            }

            // Auto-update: install a newer Nimbus first, then let the new copy do this launch.
            if (!_updateChecked && SelfUpdater.ShouldCheck(App.Settings))
            {
                _updateChecked = true;
                var updateProgress = new Progress<(string Status, double? Fraction)>(p => ShowProgress(p.Status, p.Fraction));
                if (await SelfUpdater.TryUpdateAsync(updateProgress, _cts.Token))
                {
                    SelfUpdater.HandOver(_request.ToArguments());
                    Application.Current.Shutdown();
                    return;
                }

                ShowWorking(Loc.T("Card.Starting"));
            }

            var progress = new Progress<DeployProgress>(p => ShowProgress(p.Status, p.Fraction));

            var bootstrapper = new Bootstrapper(App.Settings, App.StateStore, RobloxDeployment.CreateDefault());
            string versionDir = await bootstrapper.EnsureRobloxAsync(progress, _cts.Token);

            StatusText.Text = Loc.T("Card.StartingRoblox");
            Progress.IsIndeterminate = true;

            Mutex? multiInstance = App.Settings.MultiInstance ? RobloxLauncher.ClaimMultiInstance() : null;
            var launchedUtc = DateTime.UtcNow;
            using var roblox = RobloxLauncher.Start(versionDir, _request);
            RobloxLauncher.ApplyPriority(roblox, App.Settings.Performance.Priority);

            // Fast launch: get out of the way the moment Roblox is running.
            Hide();

            if (ActivityWatcher.IsNeeded(App.Settings))
                await new ActivityWatcher(App.Settings, App.StateStore).RunAsync(roblox, launchedUtc);
            else if (multiInstance is not null)
                await roblox.WaitForExitAsync(); // the name Roblox checks must stay taken while this client is open

            multiInstance?.Dispose();

            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            Logger.Info(LogIdent, "Launch cancelled");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            Show();
            ShowError(ex);
        }
    }

    private void ShowProgress(string status, double? fraction)
    {
        StatusText.Text = status;
        Progress.IsIndeterminate = fraction is null;
        if (fraction is { } value)
        {
            Progress.Value = value;
            if (!IsVisible)
                Show(); // "Hidden unless updating": a download is happening, so show it
        }
    }

    private void ShowWorking(string status)
    {
        StatusText.Text = status;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        ErrorPanel.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        LogButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = Loc.T("Card.Cancel");
    }

    private void ShowError(Exception ex)
    {
        StatusText.Text = Loc.T("Card.Failed");
        Progress.Visibility = Visibility.Collapsed;
        ErrorText.Text = ex switch
        {
            HttpRequestException => Loc.T("Card.Offline"),
            TimeoutException => Loc.T("Card.Timeout"),
            IOException io when io.HResult == unchecked((int)0x80070070) => Loc.T("Card.DiskFull"),
            _ => ex.Message,
        };
        ErrorPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
        LogButton.Visibility = Logger.CurrentFile is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = Loc.T("Card.Close");
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        await RunAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_request is null)
            Close(); // preview
        else if (ErrorPanel.Visibility == Visibility.Visible)
            Application.Current.Shutdown();
        else
            _cts.Cancel();
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        if (Logger.CurrentFile is { } log)
            Process.Start(new ProcessStartInfo(log) { UseShellExecute = true });
    }
}
