using System.Windows;
using Nimbus.Core;
using Nimbus.Installation;

namespace Nimbus.UI;

/// <summary>First run (or a newer download): installs or updates Nimbus, then opens the installed copy.</summary>
public partial class WelcomeWindow : Window
{
    private const string LogIdent = "Welcome";

    public WelcomeWindow()
    {
        InitializeComponent();
        Glass.Attach(this);
        LanguageName.Text = Loc.Languages.First(l => l.Code == Loc.Current).NativeName;

        if (Installer.IsInstalled)
        {
            TitleText.Text = Loc.T("Welcome.UpdateTitle");
            LeadText.Text = Loc.T("Welcome.UpdateLead", Installer.InstalledVersion, AppInfo.Version);
            InstallButton.Content = Loc.T("Welcome.UpdateTitle");
            HintText.Text = Loc.T("Welcome.UpdateHint");
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try
        {
            bool fresh = !Installer.IsInstalled;
            await Task.Run(() => Installer.Install(App.StateStore, App.Settings));
            Installer.StartInstalled(fresh ? "-thanks" : "");
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
            ErrorText.Text = ex switch
            {
                IOException or UnauthorizedAccessException => Loc.T("Welcome.FilesInUse"),
                _ => ex.Message,
            };
            ErrorPanel.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (LanguageList.Visibility == Visibility.Visible)
        {
            LanguageList.Visibility = Visibility.Collapsed;
            return;
        }

        LanguageList.Children.Clear();
        foreach (var language in Loc.Languages)
        {
            var button = new System.Windows.Controls.Button { Content = language.NativeName, Margin = new Thickness(4) };
            if (language.Code == Loc.Current)
                button.Style = (Style)FindResource("PrimaryButton");
            button.Click += (_, _) => SwitchTo(language.Code);
            LanguageList.Children.Add(button);
        }
        LanguageList.Visibility = Visibility.Visible;
    }

    /// <summary>Saves the choice (the installed Nimbus uses it too) and reopens this window in that language.</summary>
    private void SwitchTo(string code)
    {
        if (code == Loc.Current)
        {
            LanguageList.Visibility = Visibility.Collapsed;
            return;
        }

        App.Settings.Language = code;
        App.SaveSettings();
        Loc.Use(code);

        var again = new WelcomeWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = Left, Top = Top };
        again.Show();
        Close();
    }
}
