using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Nimbus.Core;

namespace Nimbus.UI;

/// <summary>Light / dark / follow-Windows themes plus the accent color, swappable at runtime.</summary>
internal static class ThemeManager
{
    /// <summary>Second glow color, the same cyan Lumen uses.</summary>
    private static readonly Color SecondTint = Color.FromRgb(40, 190, 220);

    private static Settings? _settings;

    public static bool IsDark { get; private set; }

    public static void Initialize(Settings settings)
    {
        _settings = settings;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _settings.Theme == AppTheme.System)
                Application.Current.Dispatcher.BeginInvoke(Apply);
        };
        Apply();
    }

    public static void Apply()
    {
        if (_settings is null)
            return;

        IsDark = _settings.Theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => !WindowsUsesLightTheme(),
        };

        var colors = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Nimbus;component/UI/Themes/{(IsDark ? "Dark" : "Light")}.xaml"),
        };

        // No Mica on this Windows version: an opaque base so nothing renders black.
        if (!Glass.IsSupported)
            colors["BaseTintBrush"] = colors["BaseSolidBrush"];

        Color accent = ParseAccent(_settings.AccentColor);
        double glowA = (double)colors["GlowAOpacity"];
        double glowB = (double)colors["GlowBOpacity"];

        colors["AccentColor"] = accent;
        colors["AccentBrush"] = Frozen(new SolidColorBrush(accent));
        colors["AccentSoftBrush"] = Frozen(new SolidColorBrush(WithAlpha(accent, 0.16)));
        colors["AccentGlowColor"] = accent;

        // Lumen's ambient glow: two soft radial gradients, no blur effects.
        colors["GlowABrush"] = Glow(accent, glowA, center: new Point(0.5, 0.02), radiusX: 0.55, radiusY: 0.6);
        colors["GlowBBrush"] = Glow(SecondTint, glowB, center: new Point(0.92, 1.0), radiusX: 0.45, radiusY: 0.5);

        colors["ProgressFillBrush"] = Frozen(new LinearGradientBrush(accent, SecondTint, 0));

        Application.Current.Resources.MergedDictionaries[0] = colors;

        foreach (Window window in Application.Current.Windows)
            Glass.Apply(window);
    }

    private static RadialGradientBrush Glow(Color color, double opacity, Point center, double radiusX, double radiusY)
    {
        var brush = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
            GradientStops =
            {
                new GradientStop(WithAlpha(color, opacity), 0),
                new GradientStop(WithAlpha(color, 0), 0.7),
            },
        };
        return Frozen(brush);
    }

    private static bool WindowsUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private static Color ParseAccent(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                return color;
        }
        catch (FormatException)
        {
        }

        return Color.FromRgb(0x0A, 0x84, 0xFF);
    }

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
