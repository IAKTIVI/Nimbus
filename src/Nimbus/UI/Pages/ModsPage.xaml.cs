using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nimbus.Core;
using Nimbus.Mods;
using Nimbus.Roblox;

namespace Nimbus.UI.Pages;

public partial class ModsPage : UserControl, IPage
{
    private const string LogIdent = "ModsPage";

    private bool _loading;
    private string? _previewedFont;

    private static readonly string PreviewFolder = Path.Combine(Path.GetTempPath(), "nimbus-font-preview");

    public ModsPage()
    {
        InitializeComponent();
        ClearOldPreviews();
        OnShown();
    }

    private static void ClearOldPreviews()
    {
        if (!Directory.Exists(PreviewFolder))
            return;

        foreach (string file in Directory.EnumerateFiles(PreviewFolder))
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* still open in another Nimbus window */ }
        }
    }

    private static ModSettings Mods => App.Settings.Mods;

    public void OnShown()
    {
        _loading = true;
        _loading = false;
        BuildSoundGallery();
        BuildCursorGallery();
        BuildFontGallery();
        Refresh();
    }

    private void Refresh()
    {
        CursorFilePanel.Visibility = CurrentCursorTile == OwnTile ? Visibility.Visible : Visibility.Collapsed;
        if (ModAssets.OwnCursor is not null)
            CursorFileRow.Description = Mods.CustomCursorName;

        FontFilePanel.Visibility = CurrentFontTile == OwnFont ? Visibility.Visible : Visibility.Collapsed;
        FontFileRow.Description = ModAssets.Font is null || Mods.FontPreset is not null ? Loc.T("Mods.NoFontFile") : Mods.CustomFontName;
        RefreshFontPreview();

        int count = Directory.Exists(Paths.Modifications)
            ? Directory.EnumerateFiles(Paths.Modifications, "*", SearchOption.AllDirectories).Count()
            : 0;
        ModsFolderRow.Description =
            Loc.T("Mods.FolderInfo") + " " +
            (count == 1 ? Loc.T("Mods.OneFile") : Loc.T("Mods.Files", count));
    }

    private void Save()
    {
        App.SaveSettings();
        ErrorPanel.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    // ---------- Death sound ----------

    private const string DefaultSound = "default", ClassicSound = "classic", OwnSoundPrefix = "own:";

    private static string CurrentSoundTile => Mods.DeathSound switch
    {
        DeathSoundChoice.Classic => ClassicSound,
        DeathSoundChoice.Nimbus when SoundPresets.Find(Mods.DeathSoundPreset) is { } preset => preset.Id,
        DeathSoundChoice.Custom when ModAssets.SelectedSound(Mods) is not null => OwnSoundPrefix + Mods.CustomSoundName,
        _ => DefaultSound,
    };

    /// <summary>One tile per sound: Roblox's two, the ones Nimbus makes, and the user's own files. Each has a play button.</summary>
    private void BuildSoundGallery()
    {
        _loading = true;
        SoundGallery.Items.Clear();
        SoundGallery.Items.Add(SoundTile(DefaultSound, Loc.T("Mods.Default"), () => RobloxSound(ModManager.DeathSoundFile)));
        SoundGallery.Items.Add(SoundTile(ClassicSound, Loc.T("Mods.ClassicOof"), () => RobloxSound(ModManager.ClassicDeathSound)));
        foreach (var preset in SoundPresets.All)
            SoundGallery.Items.Add(SoundTile(preset.Id, Loc.T("Sound." + preset.Id), () => (SoundPresets.Render(preset), ".wav")));
        foreach (string name in ModAssets.OwnSounds(Mods))
        {
            string path = Path.Combine(ModAssets.SoundsFolder, name);
            SoundGallery.Items.Add(SoundTile(OwnSoundPrefix + name, Path.GetFileNameWithoutExtension(name), () => (File.ReadAllBytes(path), Path.GetExtension(path)), removable: true));
        }

        SoundGallery.SelectedItem = SoundGallery.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == CurrentSoundTile);
        _loading = false;
    }

    private ListBoxItem SoundTile(string id, string name, Func<(byte[]? Data, string Extension)> sound, bool removable = false)
    {
        var play = new Button
        {
            Content = "\uF5B0",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 12,
            Width = 40,
            Height = 40,
            Padding = new Thickness(2, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("Mods.Listen"),
        };
        System.Windows.Automation.AutomationProperties.SetName(play, Loc.T("Mods.Listen") + " " + name);
        play.Click += (_, e) =>
        {
            e.Handled = true; // listening doesn't pick the sound
            var (data, ext) = sound();
            SoundPreview.Play(data, ext);
        };

        var item = Tile(id, name, play);
        if (removable && item.Content is Grid grid)
        {
            var remove = new Button
            {
                Content = "\uE711",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 8,
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 5, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = Loc.T("Mods.RemoveSound"),
            };
            System.Windows.Automation.AutomationProperties.SetName(remove, Loc.T("Mods.RemoveSound") + " " + name);
            remove.Click += (_, e) =>
            {
                e.Handled = true;
                RemoveSound(id[OwnSoundPrefix.Length..]);
            };
            grid.Children.Add(remove);
        }
        return item;
    }

    private void SoundGallery_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SoundGallery.SelectedItem is not ListBoxItem { Tag: string id })
            return;

        if (id == DefaultSound)
            Mods.DeathSound = DeathSoundChoice.Default;
        else if (id == ClassicSound)
            Mods.DeathSound = DeathSoundChoice.Classic;
        else if (id.StartsWith(OwnSoundPrefix, StringComparison.Ordinal))
        {
            Mods.DeathSound = DeathSoundChoice.Custom;
            Mods.CustomSoundName = id[OwnSoundPrefix.Length..];
        }
        else
        {
            Mods.DeathSound = DeathSoundChoice.Nimbus;
            Mods.DeathSoundPreset = id;
        }
        Save();
    }

    private void AddSound_Click(object sender, RoutedEventArgs e)
    {
        if (Pick(Loc.T("Mods.SoundFilter") + "|*.ogg;*.mp3;*.wav") is not { } file)
            return;

        Import(() =>
        {
            Mods.CustomSoundName = ModAssets.AddSound(file);
            Mods.DeathSound = DeathSoundChoice.Custom;
        });
        BuildSoundGallery();
    }

    private void RemoveSound(string name)
    {
        Import(() =>
        {
            ModAssets.RemoveSound(name);
            if (Mods.DeathSound == DeathSoundChoice.Custom && Mods.CustomSoundName == name)
            {
                Mods.DeathSound = DeathSoundChoice.Default;
                Mods.CustomSoundName = null;
            }
        });
        BuildSoundGallery();
    }

    /// <summary>A sound file of the installed Roblox (its original, even while a mod replaces it).</summary>
    private static (byte[]? Data, string Extension) RobloxSound(string rel)
    {
        try
        {
            return (InstalledRobloxFolder() is { } versionDir ? ModManager.ApplierFor(versionDir).ReadOriginal(rel) : null, ".ogg");
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, $"Sound preview: {ex.Message}");
            return (null, ".ogg");
        }
    }

    // ---------- Cursor ----------

    private const string DefaultTile = "default", ClassicTile = "classic", OwnTile = "own";

    private static string CurrentCursorTile => Mods.Cursor switch
    {
        CursorChoice.Default => DefaultTile,
        CursorChoice.Classic => ClassicTile,
        _ => CursorPresets.Find(Mods.CursorPreset)?.Id ?? OwnTile,
    };

    /// <summary>One picture tile per cursor: Roblox's two, Nimbus's built-in presets, and the user's own.</summary>
    private void BuildCursorGallery()
    {
        _loading = true;
        CursorGallery.Items.Clear();
        CursorGallery.Items.Add(CursorTile(DefaultTile, Loc.T("Mods.CursorDefault"), ToImage(RobloxCursor(classic: false))));
        CursorGallery.Items.Add(CursorTile(ClassicTile, Loc.T("Mods.CursorClassic"), ToImage(RobloxCursor(classic: true))));
        foreach (var preset in CursorPresets.All)
            CursorGallery.Items.Add(CursorTile(preset.Id, Loc.T("Cursor." + preset.Id), CursorPresets.Render(preset)));
        CursorGallery.Items.Add(CursorTile(OwnTile, Loc.T("Mods.CursorOwn"), ModAssets.OwnCursor is { } own ? ToImage(File.ReadAllBytes(own)) : null));

        CursorGallery.SelectedItem = CursorGallery.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == CurrentCursorTile);
        _loading = false;
    }

    private ListBoxItem CursorTile(string id, string name, ImageSource? image)
    {
        UIElement picture = image is not null
            ? new Image { Source = image, Width = 64, Height = 64, Stretch = Stretch.None }
            : new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // Roblox's arrows start at the image centre (that's the click point); nudge them so the tile looks balanced.
        if (image is not null && id is DefaultTile or ClassicTile)
            picture.RenderTransform = new TranslateTransform(-8, -10);
        return Tile(id, name, picture);
    }

    /// <summary>A gallery tile: a picture (or button) on top, the name below.</summary>
    private static ListBoxItem Tile(string id, string name, UIElement picture)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(picture);

        var label = new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 6, 8) };
        Grid.SetRow(label, 1);
        grid.Children.Add(label);

        var item = new ListBoxItem { Tag = id, Content = grid, ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(item, name);
        return item;
    }

    private void CursorGallery_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CursorGallery.SelectedItem is not ListBoxItem { Tag: string id })
            return;

        switch (id)
        {
            case DefaultTile:
                Mods.Cursor = CursorChoice.Default;
                Save();
                break;
            case ClassicTile:
                Mods.Cursor = CursorChoice.Classic;
                Save();
                break;
            case OwnTile when ModAssets.OwnCursor is null:
                if (!ChooseCursor())
                    BuildCursorGallery(); // cancelled: go back to the previous tile
                break;
            case OwnTile:
                Import(() =>
                {
                    ModAssets.UseOwnCursor();
                    Mods.Cursor = CursorChoice.Custom;
                    Mods.CursorPreset = null;
                });
                break;
            default:
                var preset = CursorPresets.Find(id)!;
                Import(() =>
                {
                    ModAssets.UsePreset(preset);
                    Mods.Cursor = CursorChoice.Custom;
                    Mods.CursorPreset = preset.Id;
                });
                break;
        }
    }

    private void ChooseCursor_Click(object sender, RoutedEventArgs e) => ChooseCursor();

    /// <summary>Lets the user pick their own image. Returns false if they cancelled.</summary>
    private bool ChooseCursor()
    {
        if (Pick(Loc.T("Appearance.PictureFilter") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico") is not { } file)
            return false;

        Import(() =>
        {
            ModAssets.ImportCursor(file);
            Mods.Cursor = CursorChoice.Custom;
            Mods.CursorPreset = null;
            Mods.CustomCursorName = Path.GetFileName(file);
        });
        BuildCursorGallery();
        Refresh();
        return true;
    }

    /// <summary>Roblox's own normal cursor (current or classic) from the installed version, for the tiles.</summary>
    private static byte[]? RobloxCursor(bool classic)
    {
        try
        {
            if (InstalledRobloxFolder() is not { } versionDir)
                return null;

            string current = ModManager.CursorFiles.Keys.Last(); // ArrowFarCursor: the everyday arrow
            return ModManager.ApplierFor(versionDir).ReadOriginal(classic ? ModManager.CursorFiles[current] : current);
        }
        catch (IOException ex)
        {
            Logger.Warn(LogIdent, $"Cursor preview: {ex.Message}");
            return null;
        }
    }

    // ---------- Font ----------

    private const string DefaultFont = "default", OwnFont = "own";

    private static string CurrentFontTile =>
        !Mods.UseCustomFont ? DefaultFont : FontPresets.Find(Mods.FontPreset)?.Id ?? OwnFont;

    /// <summary>Roblox's own font, the free fonts Nimbus can download, and the user's own file.</summary>
    private void BuildFontGallery()
    {
        _loading = true;
        FontGallery.Items.Clear();
        FontGallery.Items.Add(Tile(DefaultFont, Loc.T("Mods.FontDefault"), FontSample(null)));
        foreach (var preset in FontPresets.All)
            FontGallery.Items.Add(Tile(preset.Id, preset.Name, FontSample(FontPresets.Downloaded(preset), preset.Name)));
        FontGallery.Items.Add(Tile(OwnFont, Loc.T("Mods.FontOwn"), Mods.FontPreset is null && ModAssets.Font is { } own
            ? FontSample(own, ownFamily: Mods.CustomFontName)
            : new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }));

        FontGallery.SelectedItem = FontGallery.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == CurrentFontTile);
        _loading = false;
    }

    /// <summary>"Aa" in the font, or a download / folder icon while there's no file yet.</summary>
    private FrameworkElement FontSample(string? file, string? presetName = null, string? ownFamily = null)
    {
        var sample = new TextBlock { Text = "Aa", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (file is null)
        {
            if (presetName is not null)
            {
                sample.Text = "\uE896"; // download
                sample.FontSize = 18;
                sample.FontFamily = (FontFamily)FindResource("IconFont");
                sample.ToolTip = Loc.T("Mods.FontDownloads");
            }
            return sample;
        }

        try
        {
            // A downloaded font's file is never replaced, so reading its name from it is fine; the user's own font
            // file is (reading it locks it), so its name comes from when it was picked.
            if ((ownFamily ?? (presetName is not null ? ModAssets.FamilyName(file) : null)) is { } family)
                sample.FontFamily = PreviewFamily(file, family);
        }
        catch (Exception ex)
        {
            Logger.Warn(LogIdent, $"Font tile: {ex.Message}"); // only a preview: never stop the page from opening
        }
        return sample;
    }

    private async void FontGallery_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontGallery.SelectedItem is not ListBoxItem { Tag: string id })
            return;

        switch (id)
        {
            case DefaultFont:
                Mods.UseCustomFont = false;
                Save();
                break;
            case OwnFont when Mods.FontPreset is null && ModAssets.Font is not null:
                Mods.UseCustomFont = true;
                Save();
                break;
            case OwnFont:
                if (!ChooseFont())
                    BuildFontGallery(); // cancelled: go back to the previous tile
                break;
            default:
                var preset = FontPresets.Find(id)!;
                FontGallery.IsEnabled = false;
                try
                {
                    string file = await FontPresets.GetAsync(preset);
                    Import(() =>
                    {
                        Mods.CustomFontName = ModAssets.ImportFont(file);
                        Mods.FontPreset = preset.Id;
                        Mods.UseCustomFont = true;
                    });
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
                {
                    Logger.Warn(LogIdent, $"Font download: {ex.Message}");
                    ShowError(ex is InvalidDataException ? ex.Message : Loc.T("ModFiles.FontDownloadFailed"));
                }
                finally
                {
                    FontGallery.IsEnabled = true;
                }
                BuildFontGallery();
                break;
        }
    }

    private void ChooseFont_Click(object sender, RoutedEventArgs e) => ChooseFont();

    /// <summary>Lets the user pick their own font file. Returns false if they cancelled.</summary>
    private bool ChooseFont()
    {
        if (Pick(Loc.T("Mods.FontFilter") + "|*.ttf;*.otf") is not { } file)
            return false;

        Import(() =>
        {
            Mods.CustomFontName = ModAssets.ImportFont(file);
            Mods.FontPreset = null;
            Mods.UseCustomFont = true;
        });
        BuildFontGallery();
        Refresh();
        return true;
    }

    private void RefreshFontPreview()
    {
        if (!Mods.UseCustomFont || ModAssets.Font is not { } font)
        {
            FontPreview.Visibility = Visibility.Collapsed;
            return;
        }

        string key = font + File.GetLastWriteTimeUtc(font).Ticks;
        if (key != _previewedFont)
        {
            try
            {
                FontPreview.FontFamily = PreviewFamily(font, Mods.CustomFontName!);
                _previewedFont = key;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException)
            {
                Logger.Warn(LogIdent, $"Font preview: {ex.Message}");
            }
        }

        FontPreview.Visibility = Visibility.Visible;
    }

    /// <summary>WPF keeps font files open, so fonts are shown from a temporary copy and Nimbus's own files stay replaceable.</summary>
    private static FontFamily PreviewFamily(string font, string family)
    {
        string fileName = $"{Guid.NewGuid():N}{Path.GetExtension(font)}";
        Directory.CreateDirectory(PreviewFolder);
        File.Copy(font, Path.Combine(PreviewFolder, fileName));
        return new FontFamily(new Uri(PreviewFolder + "\\"), $"./{fileName}#{family}");
    }

    // ---------- Mods folder ----------

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.Modifications);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Modifications}\"") { UseShellExecute = false });
    }

    // ---------- Helpers ----------

    private string? Pick(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private void Import(Action import)
    {
        try
        {
            import();
            Save();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn(LogIdent, ex.Message);
            ShowError(ex.Message);
        }
    }

    private static string? InstalledRobloxFolder()
    {
        string? hash = App.StateStore.Value.InstalledVersion;
        if (!RobloxDeployment.IsValidVersionHash(hash))
            return null;

        var deployment = RobloxDeployment.CreateDefault();
        return deployment.IsInstalled(hash!) ? deployment.GetVersionDirectory(hash!) : null;
    }

    private static BitmapImage? ToImage(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
