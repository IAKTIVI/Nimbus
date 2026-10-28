using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nimbus.Core;
using Nimbus.UI.Controls;

namespace Nimbus.UI.Pages;

public partial class IntegrationsPage : UserControl, IPage
{
    private bool _loading;

    public IntegrationsPage()
    {
        InitializeComponent();
        OnShown();
    }

    private static IntegrationSettings Options => App.Settings.Integrations;

    public void OnShown()
    {
        _loading = true;
        DiscordToggle.IsChecked = Options.DiscordStatus;
        AppIdBox.Text = Options.DiscordAppId ?? "";
        JoinButtonToggle.IsChecked = Options.DiscordJoinButton;
        GameStatusToggle.IsChecked = Options.GameStatus;
        LocationToggle.IsChecked = Options.ServerLocation;
        HistoryToggle.IsChecked = Options.GameHistory;
        RejoinToggle.IsChecked = Options.AutoRejoin;
        _loading = false;
        Refresh();
        RefreshApps();
    }

    private void RefreshApps(CustomApp? expand = null)
    {
        AppsList.Children.Clear();
        foreach (var app in Options.CustomApps)
        {
            var card = new CustomAppCard(app, expanded: app == expand);
            card.Changed += App.SaveSettings;
            card.Removed += c =>
            {
                Options.CustomApps.Remove(c.App);
                App.SaveSettings();
                RefreshApps();
            };
            AppsList.Children.Add(card);
            AppsList.Children.Add(new Border { Style = (Style)FindResource("RowSeparator") });
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("Integrations.ProgramFilter") + " (*.exe)|*.exe", CheckFileExists = true, Title = Loc.T("Integrations.PickProgram") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var app = new CustomApp { Path = dialog.FileName };
        Options.CustomApps.Add(app);
        App.SaveSettings();
        RefreshApps(expand: app);
    }

    private void Refresh()
    {
        string id = AppIdBox.Text.Trim();
        bool valid = id.Length is >= 17 and <= 20 && id.All(char.IsAsciiDigit);

        AppIdRow.IsEnabled = Options.DiscordStatus;
        AppIdStatus.Text = id.Length == 0 ? Loc.T("Integrations.IdNeeded") : valid ? Loc.T("Integrations.IdGood") : Loc.T("Integrations.IdBad");
        AppIdStatus.SetResourceReference(TextBlock.ForegroundProperty, id.Length == 0 ? "TextTertiaryBrush" : valid ? "SuccessBrush" : "DangerBrush");
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        Options.DiscordStatus = DiscordToggle.IsChecked == true;
        Options.DiscordJoinButton = JoinButtonToggle.IsChecked == true;
        Options.GameStatus = GameStatusToggle.IsChecked == true;
        Options.ServerLocation = LocationToggle.IsChecked == true;
        Options.GameHistory = HistoryToggle.IsChecked == true;
        Options.AutoRejoin = RejoinToggle.IsChecked == true;
        App.SaveSettings();
        Refresh();
    }

    private void AppId_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;

        string id = AppIdBox.Text.Trim();
        Options.DiscordAppId = id.Length == 0 ? null : id;
        App.SaveSettings();
        Refresh();
    }

    private void CreateApp_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true });
}
