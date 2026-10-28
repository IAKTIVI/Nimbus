using System.Windows;
using System.Windows.Controls;

namespace Nimbus.UI.Controls;

/// <summary>One line inside a settings card: title and optional description on the left, a control on the right.</summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow));

    /// <summary>Puts the control under the text instead of to the right (for wide pickers).</summary>
    public static readonly DependencyProperty IsStackedProperty =
        DependencyProperty.Register(nameof(IsStacked), typeof(bool), typeof(SettingRow));

    public bool IsStacked
    {
        get => (bool)GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
