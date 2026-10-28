using System.Windows;
using System.Windows.Controls;
using Nimbus.Core;
using Nimbus.UI.Controls;

namespace Nimbus.UI.Pages;

/// <summary>The in-game widgets: menu key, music card and the stats line. Saved in Widgets.json, which the in-game menu also changes.</summary>
public partial class WidgetsPage : UserControl, IPage
{
    private WidgetSettings _widgets = new();
    private bool _waitingForKey;

    public WidgetsPage()
    {
        InitializeComponent();
        OnShown();
    }

    public void OnShown()
    {
        _widgets = WidgetStore.Load(App.Settings);
        MenuKeyButton.Content = WidgetStore.KeyName(_widgets.MenuKey);
        MusicToggle.IsChecked = _widgets.Shown.Contains(WidgetKind.Music);
        DimToggle.IsChecked = _widgets.DimGame;
        SnowToggle.IsChecked = _widgets.Snow;

        WidgetRows.Children.Clear();
        var kinds = UI.WidgetHost.Choices.Where(k => k != WidgetKind.Music).ToList();
        foreach (var kind in kinds)
        {
            var toggle = new CheckBox { Style = (Style)FindResource("Switch"), Tag = kind.ToString(), IsChecked = _widgets.Shown.Contains(kind) };
            toggle.Click += Widget_Click;
            WidgetRows.Children.Add(new SettingRow { Title = Loc.T("Widgets." + kind), Description = Loc.T("Widgets." + kind + "Desc"), Content = toggle });
            if (kind != kinds[^1])
                WidgetRows.Children.Add(new Border { Style = (Style)FindResource("RowSeparator") });
        }
    }

    private void Widget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string name } toggle || !Enum.TryParse<WidgetKind>(name, out var kind))
            return;
        _widgets.Shown.Remove(kind);
        if (toggle.IsChecked == true)
            _widgets.Shown.Add(kind);
        WidgetStore.Save(_widgets);
    }

    private void MenuLook_Click(object sender, RoutedEventArgs e)
    {
        _widgets.DimGame = DimToggle.IsChecked == true;
        _widgets.Snow = SnowToggle.IsChecked == true;
        WidgetStore.Save(_widgets);
    }

    private void ResetWidgets_Click(object sender, RoutedEventArgs e)
    {
        UI.CardPlacement.Reset();
    }

    /// <summary>Click, then press the key you want. Esc keeps the old one.</summary>
    private void MenuKey_Click(object sender, RoutedEventArgs e)
    {
        _waitingForKey = true;
        MenuKeyButton.Content = Loc.T("Widgets.PressAKey");
        MenuKeyButton.Focus();
    }

    private void MenuKey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_waitingForKey)
            return;
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        _waitingForKey = false;
        if (key != System.Windows.Input.Key.Escape)
        {
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0)
            {
                _widgets.MenuKey = vk;
                WidgetStore.Save(_widgets);
            }
        }
        MenuKeyButton.Content = WidgetStore.KeyName(_widgets.MenuKey);
    }

    private void MenuKey_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_waitingForKey)
            return;
        _waitingForKey = false;
        MenuKeyButton.Content = WidgetStore.KeyName(_widgets.MenuKey);
    }
}
