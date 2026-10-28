using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nimbus.Core;

namespace Nimbus.Mods;

/// <summary>Files the user picks on the Mods page, copied into Nimbus\Mods so the originals can move or be deleted.</summary>
internal static class ModAssets
{
    public const int CursorSize = 64; // Roblox's cursor images are 64x64

    /// <summary>The cursor that gets applied to Roblox (a rendered preset or a copy of the user's own image).</summary>
    public static string CursorPath => Path.Combine(Paths.ModAssets, "Cursor.png");

    /// <summary>The user's own image, kept so switching to a preset and back doesn't lose it.</summary>
    public static string OwnCursorPath => Path.Combine(Paths.ModAssets, "CursorOwn.png");

    /// <summary>The single sound file older versions kept; moved into the Sounds list on first use.</summary>
    private static string? LegacyDeathSound => Find("DeathSound");

    /// <summary>The user's own death sounds, one file each.</summary>
    public static string SoundsFolder => Path.Combine(Paths.ModAssets, "Sounds");

    /// <summary>File names of the user's own sounds, A to Z.</summary>
    public static IReadOnlyList<string> OwnSounds(ModSettings settings)
    {
        MoveLegacySound(settings);
        return Directory.Exists(SoundsFolder)
            ? Directory.EnumerateFiles(SoundsFolder)
                .Where(f => SoundExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f)!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
    }

    /// <summary>The own sound that's picked, if its file is still there.</summary>
    public static string? SelectedSound(ModSettings settings)
    {
        MoveLegacySound(settings);
        if (settings.CustomSoundName is not { Length: > 0 } name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        string path = Path.Combine(SoundsFolder, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Copies a sound into the list (renaming it if the name is taken) and returns its file name.</summary>
    public static string AddSound(string file)
    {
        RequireExtension(file, SoundExtensions, Loc.T("ModFiles.PickSound"));
        Directory.CreateDirectory(SoundsFolder);
        string name = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file).ToLowerInvariant();
        string target = Path.Combine(SoundsFolder, name + ext);
        for (int i = 2; File.Exists(target); i++)
            target = Path.Combine(SoundsFolder, $"{name} ({i}){ext}");
        File.Copy(file, target);
        return Path.GetFileName(target);
    }

    public static void RemoveSound(string name)
    {
        string path = Path.Combine(SoundsFolder, Path.GetFileName(name));
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void MoveLegacySound(ModSettings settings)
    {
        if (LegacyDeathSound is not { } legacy)
            return;
        try
        {
            Directory.CreateDirectory(SoundsFolder);
            string name = settings.CustomSoundName is { Length: > 0 } picked && picked.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                ? Path.GetFileNameWithoutExtension(picked) + Path.GetExtension(legacy)
                : "My sound" + Path.GetExtension(legacy);
            File.Move(legacy, Path.Combine(SoundsFolder, name), overwrite: true);
            settings.CustomSoundName = name;
        }
        catch (IOException ex)
        {
            Logger.Warn("ModAssets", $"Moving the old death sound: {ex.Message}");
        }
    }

    public static string? Cursor => File.Exists(CursorPath) ? CursorPath : null;

    public static string? OwnCursor => File.Exists(OwnCursorPath) ? OwnCursorPath : null;

    /// <summary>Renders a built-in preset as the active cursor. Needs a WPF (STA) thread.</summary>
    public static void UsePreset(CursorPreset preset)
    {
        SavePng(CursorPresets.Render(preset), CursorPath);
        File.SetLastWriteTimeUtc(CursorPath, DateTime.UtcNow);
    }

    /// <summary>Makes the user's own (previously imported) image the active cursor again.</summary>
    public static void UseOwnCursor()
    {
        File.Copy(OwnCursorPath, CursorPath, overwrite: true);
        File.SetLastWriteTimeUtc(CursorPath, DateTime.UtcNow);
    }

    public static string? Font => Find("CustomFont");

    public static readonly string[] SoundExtensions = [".ogg", ".mp3", ".wav"];
    public static readonly string[] FontExtensions = [".ttf", ".otf"];

    /// <summary>Copies a font in and returns its family name. Throws if the file isn't a usable font.</summary>
    public static string ImportFont(string file)
    {
        RequireExtension(file, FontExtensions, Loc.T("ModFiles.PickFont"));
        string family = FamilyName(file);
        Replace("CustomFont", file);
        return family;
    }

    /// <summary>Scales any image to fit 64x64 (centred, transparent padding), keeps it and makes it the active cursor.</summary>
    public static void ImportCursor(string file)
    {
        SavePng(FitImage(file, CursorSize), OwnCursorPath);
        UseOwnCursor();
    }

    /// <summary>Any image scaled to fit a size x size square (centred, transparent padding).</summary>
    private static BitmapSource FitImage(string file, int size)
    {
        BitmapSource source;
        try
        {
            using var stream = File.OpenRead(file);
            source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new InvalidDataException(Loc.T("ModFiles.BadImage"), ex);
        }

        double scale = Math.Min((double)size / source.PixelWidth, (double)size / source.PixelHeight);
        double width = source.PixelWidth * scale, height = source.PixelHeight * scale;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect((size - width) / 2, (size - height) / 2, width, height));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    public static string FamilyName(string fontFile)
    {
        try
        {
            var typeface = new GlyphTypeface(new Uri(Path.GetFullPath(fontFile)));
            return typeface.FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out string? name)
                ? name
                : typeface.FamilyNames.Values.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(fontFile);
        }
        catch (Exception ex) when (ex is FileFormatException or IOException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException(Loc.T("ModFiles.BadFont"), ex);
        }
    }

    private static void RequireExtension(string file, string[] allowed, string what)
    {
        if (!allowed.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(what);
    }

    private static string? Find(string baseName) =>
        Directory.Exists(Paths.ModAssets) ? Directory.EnumerateFiles(Paths.ModAssets, baseName + ".*").FirstOrDefault() : null;

    /// <summary>Copies <paramref name="file"/> to Mods\{baseName}{ext}, removing any earlier pick with another extension.</summary>
    private static void Replace(string baseName, string file)
    {
        Directory.CreateDirectory(Paths.ModAssets);
        string target = Path.Combine(Paths.ModAssets, baseName + Path.GetExtension(file).ToLowerInvariant());

        foreach (string old in Directory.EnumerateFiles(Paths.ModAssets, baseName + ".*"))
        {
            if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                File.Delete(old);
        }

        File.Copy(file, target, overwrite: true);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow); // so a re-picked file is always re-applied
    }
}
