using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Nimbus.Core;

namespace Nimbus.UI;

/// <summary>The thank-you card, shown once right after a fresh install (not after updates).</summary>
public partial class ThanksWindow : Window
{
    private const string LogIdent = "Thanks";

    public ThanksWindow()
    {
        InitializeComponent();
        Glass.Attach(this);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        StartNimbus("-player");
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        StartNimbus("-settings");
        Close();
    }

    private void Discord_Click(object sender, RoutedEventArgs e) =>
        Open(IsDiscordInstalled() ? AppInfo.DiscordAppInvite : AppInfo.DiscordInvite);

    private void GitHub_Click(object sender, RoutedEventArgs e) => Open(AppInfo.GitHubUrl);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void StartNimbus(string arguments) =>
        Process.Start(new ProcessStartInfo(Paths.CurrentExe, arguments) { UseShellExecute = false })?.Dispose();

    /// <summary>The Discord app registers discord:// links; with it, the invite opens right in the app.</summary>
    private static bool IsDiscordInstalled()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"discord\shell\open\command");
        return key is not null;
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Couldn't open {url}: {ex.Message}");
            if (url != AppInfo.DiscordAppInvite)
                return;
            Open(AppInfo.DiscordInvite); // the app link failed, so use the browser
        }
    }
}
