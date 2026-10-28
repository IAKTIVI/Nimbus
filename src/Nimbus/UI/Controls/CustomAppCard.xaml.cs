using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nimbus.Core;
using Nimbus.Integrations;

namespace Nimbus.UI.Controls;

/// <summary>One of the user's own programs on the Integrations page: header with on/off, and its options below.</summary>
public partial class CustomAppCard : UserControl
{
    private static readonly int[] Delays = [0, 5, 10, 30, 60];

    private readonly CustomApp _app;
    private bool _loading;

    /// <summary>Something changed that should be saved.</summary>
    public event Action? Changed;

    public event Action<CustomAppCard>? Removed;

    internal CustomApp App => _app;

    public CustomAppCard(CustomApp app, bool expanded = false)
    {
        _app = app;
        InitializeComponent();

        _loading = true;
        NameText.Text = app.Name;
        ToolTip = app.Path;
        AppIcon.Source = LoadIcon(app.Path);
        EnabledToggle.IsChecked = app.Enabled;
        ArgsBox.Text = app.Arguments;
        int delay = Array.IndexOf(Delays, app.DelaySeconds);
        DelayPicker.SelectedIndex = delay < 0 ? 0 : delay;
        CloseToggle.IsChecked = app.CloseWithRoblox;
        _loading = false;

        SetExpanded(expanded);
        RefreshGames();
        RefreshSummary();
    }

    private void Save()
    {
        if (_loading)
            return;
        RefreshSummary();
        Changed?.Invoke();
    }

    private void RefreshSummary()
    {
        var parts = new List<string>
        {
            !File.Exists(_app.Path) ? Loc.T("Apps.NotFound")
            : _app.Games.Count == 0 ? Loc.T("Apps.StartsWithRoblox")
            : Loc.T("Apps.StartsOnJoin", string.Join(", ", _app.Games.Select(g => g.Name))),
        };
        if (_app.DelaySeconds > 0)
            parts.Add(_app.DelaySeconds < 60 ? Loc.T("Apps.AfterSeconds", _app.DelaySeconds) : Loc.T("Apps.AfterMinute"));
        if (_app.CloseWithRoblox)
            parts.Add(Loc.T("Apps.ClosesWithRoblox"));
        SummaryText.Text = string.Join(" - ", parts);
    }

    private void SetExpanded(bool expanded)
    {
        Editor.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        EditButton.Content = expanded ? Loc.T("Apps.Done") : Loc.T("Apps.Edit");
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        bool expand = Editor.Visibility != Visibility.Visible;
        SetExpanded(expand);
        if (!expand)
            GamePicker.Visibility = Visibility.Collapsed;
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => Removed?.Invoke(this);

    private void Enabled_Click(object sender, RoutedEventArgs e)
    {
        _app.Enabled = EnabledToggle.IsChecked == true;
        Save();
    }

    private void Args_TextChanged(object sender, TextChangedEventArgs e)
    {
        _app.Arguments = ArgsBox.Text.Trim();
        Save();
    }

    private void Delay_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DelayPicker.SelectedIndex < 0)
            return;
        _app.DelaySeconds = Delays[DelayPicker.SelectedIndex];
        Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _app.CloseWithRoblox = CloseToggle.IsChecked == true;
        Save();
    }

    // ---------- Games ----------

    private void RefreshGames()
    {
        GameChips.Children.Clear();
        foreach (var game in _app.Games)
            GameChips.Children.Add(Chip(game));
        GameChips.Visibility = _app.Games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border Chip(CustomAppGame game)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(GameIcon(game.IconUrl, 22));
        row.Children.Add(new TextBlock { Text = game.Name, FontSize = 13, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });

        var remove = new Button { Padding = new Thickness(6, 0, 6, 0), Height = 24, ToolTip = Loc.T("Apps.Remove"), Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10 } };
        remove.Click += (_, _) =>
        {
            _app.Games.Remove(game);
            RefreshGames();
            RefreshHistoryGames();
            Save();
        };
        row.Children.Add(remove);

        var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(5, 4, 4, 4), Margin = new Thickness(0, 0, 8, 8), Child = row };
        chip.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        return chip;
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        bool show = GamePicker.Visibility != Visibility.Visible;
        GamePicker.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            RefreshHistoryGames();
    }

    /// <summary>Recently played games (from the History page) that aren't picked yet, newest first.</summary>
    private void RefreshHistoryGames()
    {
        HistoryGames.Children.Clear();
        var games = GameHistory.Load()
            .Where(h => _app.Games.All(g => g.PlaceId != h.PlaceId))
            .DistinctBy(h => h.PlaceId)
            .Take(12)
            .ToList();

        foreach (var entry in games)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(GameIcon(entry.IconUrl, 22));
            content.Children.Add(new TextBlock { Text = entry.Name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button { Content = content, Padding = new Thickness(6, 0, 12, 0), Height = 34, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += async (_, _) => await AddGameAsync(entry.PlaceId, entry.Name, entry.IconUrl);
            HistoryGames.Children.Add(button);
        }

        HistoryLabel.Visibility = HistoryGames.Visibility = games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void AddLink_Click(object sender, RoutedEventArgs e) => await AddLinkAsync();

    private async void LinkBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await AddLinkAsync();
    }

    private async Task AddLinkAsync()
    {
        LinkStatus.Visibility = Visibility.Collapsed;
        if (CustomAppRunner.ParseGameLink(LinkBox.Text) is not { } placeId)
        {
            LinkStatus.Text = Loc.T("Apps.BadLink");
            LinkStatus.Visibility = Visibility.Visible;
            return;
        }

        AddLinkButton.IsEnabled = false;
        await AddGameAsync(placeId, null, null);
        AddLinkButton.IsEnabled = true;
        LinkBox.Text = "";
    }

    private async Task AddGameAsync(long placeId, string? name, string? iconUrl)
    {
        if (_app.Games.Any(g => g.PlaceId == placeId))
            return;

        // The experience id makes its other places (teleports) count too; name and icon for links.
        var details = await RobloxApi.GetGameAsync(placeId);
        _app.Games.Add(new CustomAppGame
        {
            PlaceId = placeId,
            UniverseId = details?.UniverseId ?? 0,
            Name = name ?? details?.Name ?? $"Place {placeId}",
            IconUrl = iconUrl ?? details?.IconUrl,
        });
        RefreshGames();
        RefreshHistoryGames();
        Save();
    }

    private Border GameIcon(string? url, double size)
    {
        var icon = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = (int)(size * 2);
            image.EndInit();
            icon.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        return icon;
    }

    /// <summary>The program's own icon, as Explorer shows it.</summary>
    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
