using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.UI.Pages;

public partial class HistoryPage : UserControl, IPage
{
    public HistoryPage()
    {
        InitializeComponent();
        OnShown();
    }

    public async void OnShown()
    {
        ShowMostPlayed();
        if (await Playtime.MergeByExperienceAsync())
            ShowMostPlayed(); // places of the same game were listed separately; now they're one
        List.Children.Clear();
        var entries = GameHistory.Load();
        ClearButton.IsEnabled = entries.Count > 0;

        if (entries.Count == 0)
        {
            List.Children.Add(new TextBlock
            {
                Text = App.Settings.Integrations.GameHistory
                    ? Loc.T("History.Empty")
                    : Loc.T("History.Off"),
                Style = (Style)FindResource("CaptionText"),
                Margin = new Thickness(18, 16, 18, 16),
            });
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
                List.Children.Add(new Border { Style = (Style)FindResource("RowSeparator") });
            List.Children.Add(Row(entries[i]));
        }
    }

    private const int MostPlayedCount = 5;

    private void ShowMostPlayed()
    {
        MostPlayedList.Children.Clear();
        var top = Playtime.Top(MostPlayedCount);
        MostPlayedSection.Visibility = top.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Header.Margin = new Thickness(0, top.Count == 0 ? 16 : 0, 0, 0); // the "Recent" label already makes room
        if (top.Count == 0)
            return;

        double most = top[0].Seconds;
        for (int i = 0; i < top.Count; i++)
        {
            if (i > 0)
                MostPlayedList.Children.Add(new Border { Style = (Style)FindResource("RowSeparator") });
            MostPlayedList.Children.Add(PlaytimeRow(top[i], top[i].Seconds / most));
        }
    }

    /// <summary>Icon, name, sessions, a bar compared with the most played game, and the total time.</summary>
    private Grid PlaytimeRow(PlaytimeEntry entry, double share)
    {
        var grid = new Grid { Margin = new Thickness(18, 12, 18, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Icon(entry.IconUrl, 40));

        var text = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = entry.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        var sub = new TextBlock
        {
            Text = (entry.Sessions == 1 ? Loc.T("Stats.OneSession") : Loc.T("Stats.Sessions", entry.Sessions)) + " - " + Ago(entry.LastPlayed),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        text.Children.Add(sub);

        var bar = new Grid { Height = 4, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Width = 320 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.02), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Min(share, 0.98), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        bar.Children.Add(fill);
        text.Children.Add(bar);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = Playtime.Format(entry.Seconds), Style = (Style)FindResource("ValueText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
        var play = new Button { Content = Loc.T("History.Play") };
        play.Click += (_, _) => Launch(GameHistory.PlayLink(entry.PlaceId));
        right.Children.Add(play);
        var remove = new Button
        {
            Content = "",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 11,
            Width = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = Loc.T("History.RemoveGame"),
        };
        System.Windows.Automation.AutomationProperties.SetName(remove, Loc.T("History.RemoveGame"));
        remove.Click += (_, _) =>
        {
            Playtime.Remove(entry.PlaceId);
            ShowMostPlayed();
        };
        right.Children.Add(remove);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return grid;
    }

    private static Border Icon(string? url, double size)
    {
        var icon = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 5), ClipToBounds = true };
        icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = (int)size * 2;
            image.EndInit();
            icon.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        return icon;
    }

    private Grid Row(HistoryEntry entry)
    {
        var grid = new Grid { Margin = new Thickness(18, 12, 18, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(10), ClipToBounds = true };
        icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        if (entry.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = 96;
            image.EndInit();
            icon.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        grid.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = entry.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        var details = new List<string>();
        if (entry.Creator is { } creator)
            details.Add(Loc.T("History.By", creator));
        if (entry.Location is { } location)
            details.Add(location);
        details.Add(Ago(entry.JoinedAt));
        var sub = new TextBlock { Text = string.Join(" - ", details), FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(entry.JobId))
        {
            var rejoin = new Button { Content = Loc.T("Watcher.JoinServer2"), Style = (Style)FindResource("PrimaryButton") };
            rejoin.Click += (_, _) => Launch(GameHistory.RejoinLink(entry.PlaceId, entry.JobId));
            buttons.Children.Add(rejoin);
        }
        var play = new Button { Content = Loc.T("History.Play"), Margin = new Thickness(8, 0, 0, 0) };
        play.Click += (_, _) => Launch(GameHistory.PlayLink(entry.PlaceId));
        buttons.Children.Add(play);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return grid;
    }

    private static void Launch(string link) =>
        Process.Start(new ProcessStartInfo(Paths.CurrentExe) { UseShellExecute = false, ArgumentList = { link } });

    private static string Ago(DateTimeOffset time)
    {
        var span = DateTimeOffset.UtcNow - time;
        return span.TotalMinutes < 1 ? Loc.T("History.JustNow")
            : span.TotalHours < 1 ? Loc.T("History.MinAgo", (int)span.TotalMinutes)
            : span.TotalDays < 1 ? Loc.T("History.HoursAgo", (int)span.TotalHours)
            : span.TotalDays < 2 ? Loc.T("History.Yesterday")
            : time.LocalDateTime.ToString("d MMM", System.Globalization.CultureInfo.CurrentUICulture);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        GameHistory.Clear(); // game hours stay: they have their own button
        OnShown();
    }

    private void ClearPlaytime_Click(object sender, RoutedEventArgs e)
    {
        Playtime.Clear();
        ShowMostPlayed();
    }
}
