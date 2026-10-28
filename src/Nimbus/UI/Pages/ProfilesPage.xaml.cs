using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nimbus.Core;
using Nimbus.UI.Controls;

namespace Nimbus.UI.Pages;

public partial class ProfilesPage : UserControl, IPage
{
    private static readonly Dictionary<string, string> PresetDescriptions = new()
    {
        [Profiles.Potato.Name] = Loc.T("Profiles.PotatoDesc"),
        [Profiles.Balanced.Name] = Loc.T("Profiles.BalancedDesc"),
        [Profiles.Quality.Name] = Loc.T("Profiles.QualityDesc"),
    };

    /// <summary>The custom profile currently being renamed, if any.</summary>
    private string? _renaming;

    public ProfilesPage()
    {
        InitializeComponent();
        OnShown();
    }

    public void OnShown()
    {
        _renaming = null;
        Rebuild();
    }

    private void Rebuild()
    {
        var current = Profiles.Current(App.Settings);
        BuildPresets(current);
        BuildCustomList(current);
    }

    private void BuildPresets(Profile? current)
    {
        PresetGrid.Children.Clear();
        for (int i = 0; i < Profiles.BuiltIn.Count; i++)
        {
            var profile = Profiles.BuiltIn[i];
            bool active = current == profile;

            var card = new Border
            {
                Style = (Style)FindResource("GlassPanel"),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, i == Profiles.BuiltIn.Count - 1 ? 0 : 6, 0),
            };
            if (active)
                card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

            var stack = new DockPanel { LastChildFill = false };

            var title = new TextBlock { Text = profile.Name, FontSize = 16, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(title, Dock.Top);
            stack.Children.Add(title);

            var description = new TextBlock
            {
                Text = PresetDescriptions[profile.Name],
                Style = (Style)FindResource("CaptionText"),
                Margin = new Thickness(0, 6, 0, 0),
                MinHeight = 64,
                LineHeight = 17,
            };
            DockPanel.SetDock(description, Dock.Top);
            stack.Children.Add(description);

            var button = active
                ? new Button { Content = Loc.T("Profiles.InUse"), IsEnabled = false, Margin = new Thickness(0, 14, 0, 0) }
                : new Button { Content = Loc.T("Profiles.Use"), Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 14, 0, 0) };
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => Use(profile);
            DockPanel.SetDock(button, Dock.Bottom);
            stack.Children.Add(button);

            card.Child = stack;
            PresetGrid.Children.Add(card);
        }
    }

    private void BuildCustomList(Profile? current)
    {
        CustomList.Children.Clear();

        if (App.Settings.Profiles.Count == 0)
        {
            CustomList.Children.Add(new TextBlock
            {
                Text = Loc.T("Profiles.NoneSaved"),
                Style = (Style)FindResource("CaptionText"),
                Margin = new Thickness(18, 14, 18, 14),
            });
            return;
        }

        for (int i = 0; i < App.Settings.Profiles.Count; i++)
        {
            if (i > 0)
                CustomList.Children.Add(new Border { Style = (Style)FindResource("RowSeparator") });

            var profile = App.Settings.Profiles[i];
            CustomList.Children.Add(profile.Name == _renaming ? RenameRow(profile) : ProfileRow(profile, current == profile));
        }
    }

    private SettingRow ProfileRow(Profile profile, bool active)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        var use = active
            ? new Button { Content = Loc.T("Profiles.InUse"), IsEnabled = false }
            : new Button { Content = Loc.T("Profiles.Use"), Style = (Style)FindResource("PrimaryButton") };
        use.Click += (_, _) => Use(profile);

        var rename = new Button { Content = Loc.T("Profiles.Rename"), Margin = new Thickness(8, 0, 0, 0) };
        rename.Click += (_, _) =>
        {
            _renaming = profile.Name;
            Rebuild();
        };

        var delete = new Button { Content = Loc.T("Profiles.Delete"), Style = (Style)FindResource("DangerButton"), Margin = new Thickness(8, 0, 0, 0) };
        delete.Click += (_, _) =>
        {
            App.Settings.Profiles.Remove(profile);
            App.SaveSettings();
            Rebuild();
        };

        buttons.Children.Add(use);
        buttons.Children.Add(rename);
        buttons.Children.Add(delete);

        return new SettingRow { Title = profile.Name, Description = Profiles.Summarize(profile.Engine), Content = buttons };
    }

    private FrameworkElement RenameRow(Profile profile)
    {
        var box = new TextBox { Text = profile.Name, Width = 220 };
        var error = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");

        void Commit()
        {
            string name = box.Text.Trim();
            if (Profiles.ValidateName(App.Settings, name, renaming: profile.Name) is { } problem)
            {
                error.Text = problem;
                error.Visibility = Visibility.Visible;
                return;
            }

            int index = App.Settings.Profiles.IndexOf(profile);
            App.Settings.Profiles[index] = profile with { Name = name };
            App.SaveSettings();
            _renaming = null;
            Rebuild();
        }

        void Cancel()
        {
            _renaming = null;
            Rebuild();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Commit();
            else if (e.Key == Key.Escape)
                Cancel();
        };

        var save = new Button { Content = Loc.T("Profiles.Save"), Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) => Commit();
        var cancel = new Button { Content = Loc.T("Card.Cancel"), Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Cancel();

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(box);
        line.Children.Add(save);
        line.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18, 12, 18, 12) };
        panel.Children.Add(line);
        panel.Children.Add(error);

        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        });
        return panel;
    }

    private void Use(Profile profile)
    {
        Profiles.Apply(App.Settings, profile);
        App.SaveSettings();
        Rebuild();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveNew();

    private void NewName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SaveNew();
    }

    private void NewName_TextChanged(object sender, TextChangedEventArgs e)
    {
        NewNameHint.Visibility = NewNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NameError.Visibility = Visibility.Collapsed;
    }

    private void SaveNew()
    {
        string name = NewNameBox.Text.Trim();
        if (Profiles.ValidateName(App.Settings, name) is { } problem)
        {
            NameError.Text = problem;
            NameError.Visibility = Visibility.Visible;
            return;
        }

        App.Settings.Profiles.Add(new Profile(name, App.Settings.Engine with { }));
        App.SaveSettings();
        NewNameBox.Text = "";
        Rebuild();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Use(new Profile(Loc.T("Profiles.RobloxDefaults"), new EngineSettings()));
}
