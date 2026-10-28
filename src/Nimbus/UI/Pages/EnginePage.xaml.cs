using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nimbus.Core;
using Nimbus.Roblox;

namespace Nimbus.UI.Pages;

public partial class EnginePage : UserControl, IPage
{
    // Picker index -> setting value (index 0 is always "Automatic")
    private static readonly int?[] MsaaValues = [null, 1, 2, 4];
    private static readonly int?[] TextureValues = [null, 0, 1, 2, 3];
    private static readonly int?[] QualityValues = [null, 1, 5, 10, 15, 21];

    private readonly DispatcherTimer _saveFlags = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _loading;

    public EnginePage()
    {
        InitializeComponent();
        AllowlistText.Text = string.Join(Environment.NewLine, FastFlags.Allowlist.Order(StringComparer.Ordinal));
        _saveFlags.Tick += (_, _) =>
        {
            _saveFlags.Stop();
            App.SaveSettings();
        };
        OnShown();
    }

    public void OnShown()
    {
        _loading = true;

        var e = App.Settings.Engine;
        ApiPicker.SelectedIndex = (int)e.GraphicsApi;
        MsaaPicker.SelectedIndex = IndexOf(MsaaValues, e.Msaa);
        TexturePicker.SelectedIndex = IndexOf(TextureValues, e.TextureQuality);
        QualityPicker.SelectedIndex = IndexOf(QualityValues, e.QualityLevel);

        GraySkyToggle.IsChecked = e.GraySky;
        HideGrassToggle.IsChecked = e.HideGrass;
        StillGrassToggle.IsChecked = e.StillGrass;
        LowDetailToggle.IsChecked = e.LowDetailMeshes;
        FreezeLightingToggle.IsChecked = e.FreezeLighting;
        WaterToggle.IsChecked = App.Settings.Mods.RemoveWaterTexture;
        FullscreenToggle.IsChecked = e.ExclusiveFullscreen;
        FullResolutionToggle.IsChecked = e.FullResolution;

        FlagsBox.Text = App.Settings.CustomFlags;

        _loading = false;
        RefreshProfile();
        ValidateFlags();
    }

    private void Picker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            SaveEngine();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            SaveEngine();
    }

    /// <summary>A file mod, not part of the profile: it's applied to the Roblox files before each launch.</summary>
    private void Water_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Mods.RemoveWaterTexture = WaterToggle.IsChecked == true;
        App.SaveSettings();
    }

    private void SaveEngine()
    {
        App.Settings.Engine = new EngineSettings
        {
            GraphicsApi = (GraphicsApi)Math.Max(0, ApiPicker.SelectedIndex),
            Msaa = MsaaValues[Math.Max(0, MsaaPicker.SelectedIndex)],
            TextureQuality = TextureValues[Math.Max(0, TexturePicker.SelectedIndex)],
            QualityLevel = QualityValues[Math.Max(0, QualityPicker.SelectedIndex)],
            GraySky = GraySkyToggle.IsChecked == true,
            HideGrass = HideGrassToggle.IsChecked == true,
            StillGrass = StillGrassToggle.IsChecked == true,
            LowDetailMeshes = LowDetailToggle.IsChecked == true,
            FreezeLighting = FreezeLightingToggle.IsChecked == true,
            ExclusiveFullscreen = FullscreenToggle.IsChecked == true,
            FullResolution = FullResolutionToggle.IsChecked == true,
        };
        App.SaveSettings();
        RefreshProfile();
    }

    private void RefreshProfile() => ProfileName.Text = Profiles.Current(App.Settings)?.Name ?? Loc.T("Engine.CustomProfile");

    private void FlagsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;

        // Kept in memory right away; written to disk once typing pauses (and when the window closes).
        App.Settings.CustomFlags = FlagsBox.Text;
        ValidateFlags();
        _saveFlags.Stop();
        _saveFlags.Start();
    }

    private void ValidateFlags()
    {
        try
        {
            FlagStatus.ItemsSource = FastFlags.ParseCustom(FlagsBox.Text);
            FlagError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            FlagStatus.ItemsSource = null;
            FlagErrorText.Text = Loc.T("Engine.InvalidJson") + " " + Describe(ex);
            FlagError.Visibility = Visibility.Visible;
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        JsonException { LineNumber: { } line } => Loc.T("Engine.CheckLine", line + 1),
        _ => ex.Message,
    };

    private void ChangeProfile_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("Profiles");

    private void Allowlist_Click(object sender, RoutedEventArgs e)
    {
        bool show = AllowlistText.Visibility != Visibility.Visible;
        AllowlistText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AllowlistButton.Content = show ? Loc.T("Engine.HideAllowed") : Loc.T("Engine.ShowAllowed");
    }

    /// <summary>The picker index for a value; values set elsewhere snap to the nearest option.</summary>
    private static int IndexOf(int?[] options, int? value)
    {
        if (value is null)
            return 0;

        int best = 1;
        for (int i = 1; i < options.Length; i++)
        {
            if (Math.Abs(options[i]!.Value - value.Value) < Math.Abs(options[best]!.Value - value.Value))
                best = i;
        }

        return best;
    }
}
