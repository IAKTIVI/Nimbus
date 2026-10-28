using System.Text.Json;
using System.Text.Json.Nodes;
using Nimbus.Core;

namespace Nimbus.Mods;

/// <summary>Turns the Mods page settings plus the user's mods folder into file changes in the Roblox install.</summary>
internal static class ModManager
{
    private const string LogIdent = "Mods";

    public const string WaterFolder = @"PlatformContent\pc\textures\water";
    public const string DeathSoundFile = @"content\sounds\ouch.ogg";
    public const string ClassicDeathSound = @"content\sounds\oof.ogg";
    public const string FontFamilies = @"content\fonts\families";

    /// <summary>Current cursor file -> the classic file Roblox still ships.</summary>
    public static readonly IReadOnlyDictionary<string, string> CursorFiles = new Dictionary<string, string>
    {
        [@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png"] = @"content\textures\ArrowCursor.png",
        [@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png"] = @"content\textures\ArrowFarCursor.png",
    };

    public static ModApplier ApplierFor(string versionDirectory) =>
        new(versionDirectory, Path.Combine(Paths.ModBackups, Path.GetFileName(versionDirectory)));

    /// <summary>Runs before every launch. Never throws: a broken mod must not stop Roblox from starting.</summary>
    public static void Apply(string versionDirectory, Settings settings)
    {
        try
        {
            var applier = ApplierFor(versionDirectory);
            var mods = Build(settings.Mods, applier, Paths.Modifications);
            applier.Apply(mods);
        }
        catch (Exception ex)
        {
            Logger.Error(LogIdent, ex);
        }
    }

    /// <summary>Deletes saved originals for Roblox versions that are no longer installed.</summary>
    public static void CleanupBackups(string keepVersionHash)
    {
        if (!Directory.Exists(Paths.ModBackups))
            return;

        foreach (string dir in Directory.EnumerateDirectories(Paths.ModBackups))
        {
            if (string.Equals(Path.GetFileName(dir), keepVersionHash, StringComparison.OrdinalIgnoreCase))
                continue;

            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* next time */ }
        }
    }

    public static Dictionary<string, ModFile> Build(ModSettings settings, ModApplier originals, string userModsFolder)
    {
        var mods = new Dictionary<string, ModFile>(StringComparer.OrdinalIgnoreCase);

        if (settings.RemoveWaterTexture)
        {
            foreach (string file in originals.ListOriginals(WaterFolder, "*.dds"))
                mods[file] = new ModFile.Delete();
        }

        switch (settings.DeathSound)
        {
            case DeathSoundChoice.Classic when originals.ReadOriginal(ClassicDeathSound) is { } oof:
                mods[DeathSoundFile] = new ModFile.Write(oof);
                break;
            case DeathSoundChoice.Nimbus when SoundPresets.Find(settings.DeathSoundPreset) is { } preset:
                mods[DeathSoundFile] = new ModFile.Write(SoundPresets.Render(preset));
                break;
            case DeathSoundChoice.Custom when ModAssets.SelectedSound(settings) is { } sound:
                mods[DeathSoundFile] = new ModFile.Copy(sound);
                break;
        }

        foreach (var (current, classic) in CursorFiles)
        {
            switch (settings.Cursor)
            {
                case CursorChoice.Classic when originals.ReadOriginal(classic) is { } image:
                    mods[current] = new ModFile.Write(image);
                    break;
                case CursorChoice.Custom when ModAssets.Cursor is { } cursor:
                    mods[current] = new ModFile.Copy(cursor);
                    break;
            }
        }

        if (settings.UseCustomFont && ModAssets.Font is { } font)
            AddFont(mods, originals, font);

        AddUserMods(mods, userModsFolder);
        return mods;
    }

    /// <summary>
    /// Copies the font in as content\fonts\CustomFont.ttf (or .otf) and points every face of every font family at it,
    /// so all of Roblox's text uses it.
    /// </summary>
    private static void AddFont(Dictionary<string, ModFile> mods, ModApplier originals, string font)
    {
        string fileName = "CustomFont" + Path.GetExtension(font).ToLowerInvariant();
        mods[@"content\fonts\" + fileName] = new ModFile.Copy(font);

        foreach (string family in originals.ListOriginals(FontFamilies, "*.json"))
        {
            if (originals.ReadOriginal(family) is not { } json)
                continue;

            try
            {
                mods[family] = new ModFile.Write(PointFamilyAt(json, "rbxasset://fonts/" + fileName));
            }
            catch (JsonException ex)
            {
                Logger.Warn(LogIdent, $"Skipped font family {Path.GetFileName(family)}: {ex.Message}");
            }
        }
    }

    internal static byte[] PointFamilyAt(byte[] familyJson, string assetId)
    {
        var root = JsonNode.Parse(familyJson) ?? throw new JsonException("empty font family");
        if (root["faces"] is JsonArray faces)
        {
            foreach (var face in faces.OfType<JsonObject>())
                face["assetId"] = assetId;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root, NimbusJson.Default.JsonNode);
    }

    /// <summary>Every file in the user's mods folder replaces the Roblox file at the same relative path.</summary>
    private static void AddUserMods(Dictionary<string, ModFile> mods, string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(folder, file);

            // FastFlags own this file; Nimbus writes it from the Engine page.
            if (rel.StartsWith(@"ClientSettings\", StringComparison.OrdinalIgnoreCase))
                continue;

            mods[rel] = new ModFile.Copy(file);
        }
    }
}
