using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nimbus.Core;

namespace Nimbus.UI.Pages;

public partial class AppearancePage : UserControl, IPage
{
    /// <summary>Apple-style system colors plus Lumen's purple.</summary>
    private static readonly (string Name, string Hex)[] Presets =
    [
        ("Blue", "#0A84FF"), ("Purple", "#7C5CFF"), ("Pink", "#FF375F"), ("Orange", "#FF9F0A"),
        ("Green", "#30D158"), ("Teal", "#40C8E0"), ("Graphite", "#8E8E93"),
    ];

    private bool _loading;

    public AppearancePage()
    {
        InitializeComponent();
        foreach (var (name, hex) in Presets)
            Swatches.Children.Add(Swatch(name, hex));
        foreach (var language in Loc.Languages)
            LanguagePicker.Items.Add(new ListBoxItem { Content = language.NativeName, Tag = language.Code });
        OnShown();
    }

    public void OnShown()
    {
        _loading = true;
        ThemePicker.SelectedIndex = (int)App.Settings.Theme;
        LanguagePicker.SelectedIndex = Array.FindIndex(Loc.Languages, l => l.Code == Loc.Current);
        LaunchPicker.SelectedIndex = (int)App.Settings.LaunchStyle;
        HexBox.Text = App.Settings.AccentColor;
        _loading = false;
        RefreshSwatches();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => new LaunchWindow(null).Preview();

    private Button Swatch(string name, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var button = new Button
        {
            Tag = hex,
            ToolTip = Loc.T("Appearance.Color" + name),
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = SwatchTemplate(new SolidColorBrush(color)),
        };
        button.Click += (_, _) => SetAccent(hex);
        return button;
    }

    /// <summary>A filled circle with a ring around it when it's the current accent.</summary>
    private static ControlTemplate SwatchTemplate(Brush fill)
    {
        var ring = new FrameworkElementFactory(typeof(Border), "Ring");
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        ring.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        ring.SetValue(Border.PaddingProperty, new Thickness(3));

        var dot = new FrameworkElementFactory(typeof(Border));
        dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        dot.SetValue(Border.BackgroundProperty, fill);
        ring.AppendChild(dot);

        return new ControlTemplate(typeof(Button)) { VisualTree = ring };
    }

    private void RefreshSwatches()
    {
        foreach (Button swatch in Swatches.Children)
        {
            swatch.ApplyTemplate();
            if (swatch.Template.FindName("Ring", swatch) is Border ring)
            {
                bool current = string.Equals((string)swatch.Tag, App.Settings.AccentColor, StringComparison.OrdinalIgnoreCase);
                if (current)
                    ring.SetResourceReference(Border.BorderBrushProperty, "TextPrimaryBrush");
                else
                    ring.BorderBrush = Brushes.Transparent;
            }
        }
    }

    private void SetAccent(string hex)
    {
        App.Settings.AccentColor = hex.ToUpperInvariant();
        App.SaveSettings();
        ThemeManager.Apply();

        _loading = true;
        HexBox.Text = App.Settings.AccentColor;
        _loading = false;
        RefreshSwatches();
    }

    private void Hex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;

        string text = HexBox.Text.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$"))
            SetAccent(text);
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguagePicker.SelectedItem is not ListBoxItem { Tag: string code } || code == Loc.Current)
            return;

        // All text is set when a window opens, so Nimbus restarts in the new language.
        App.Settings.Language = code;
        App.SaveSettings();
        ((App)Application.Current).Restart();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemePicker.SelectedIndex < 0)
            return;

        App.Settings.Theme = (AppTheme)ThemePicker.SelectedIndex;
        App.SaveSettings();
        ThemeManager.Apply();
    }

    private void Launch_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LaunchPicker.SelectedIndex < 0)
            return;

        App.Settings.LaunchStyle = (LaunchStyle)LaunchPicker.SelectedIndex;
        App.SaveSettings();
    }
}
