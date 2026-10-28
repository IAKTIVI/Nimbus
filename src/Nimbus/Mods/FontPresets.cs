using System.Security.Cryptography;
using Nimbus.Core;

namespace Nimbus.Mods;

/// <summary>A free font (SIL Open Font License) Nimbus downloads the first time it's picked.</summary>
public sealed record FontPreset(string Id, string Name, string Url, string Sha256);

/// <summary>
/// Fonts offered on the Mods page. They aren't built into Nimbus (it stays small): each is downloaded once into
/// Nimbus\Mods\Fonts from a fixed version, and checked against its hash so nothing else can sneak in.
/// </summary>
internal static class FontPresets
{
    private const string LogIdent = "FontPresets";
    private const string GoogleFonts = "https://raw.githubusercontent.com/google/fonts/9862abc066c9b83bf84c3e8ed1fc2d8d48f9bbf0/ofl/";

    public static IReadOnlyList<FontPreset> All { get; } =
    [
        new("monocraft", "Monocraft", "https://raw.githubusercontent.com/IdreesInc/Monocraft/v4.2.1/dist/Monocraft-ttf/Monocraft.ttf",
            "262e31822e0a75be567b02320bfa092143d871e8a273e529d1c5ab0c04cd215f"),
        new("pressstart2p", "Press Start 2P", GoogleFonts + "pressstart2p/PressStart2P-Regular.ttf",
            "034c77f1f05ec89421e4a63f0e3a4ca1ecf852cc6d2bf611f126f275728e017d"),
        new("comicneue", "Comic Neue", GoogleFonts + "comicneue/ComicNeue-Bold.ttf",
            "3e7e5fccfd7e0788f317b43312151c1bd5cf058c9697a8d83eac3939050bd61e"),
        new("bangers", "Bangers", GoogleFonts + "bangers/Bangers-Regular.ttf",
            "4160a7311de9342674cce9160cde9fcbb30f48190397d86ff1b70b455af65824"),
        new("orbitron", "Orbitron", GoogleFonts + "orbitron/Orbitron%5Bwght%5D.ttf",
            "f42db2dd16e642258e35782916eceb1dcdbea06fb958d77ad71dc5963587e8fd"),
    ];

    public static FontPreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    private static string Folder => Path.Combine(Paths.ModAssets, "Fonts");

    public static string PathOf(FontPreset preset) => Path.Combine(Folder, preset.Id + ".ttf");

    /// <summary>The downloaded file, or null if it isn't here yet.</summary>
    public static string? Downloaded(FontPreset preset) => File.Exists(PathOf(preset)) ? PathOf(preset) : null;

    /// <summary>Downloads the font if needed and returns its file. Throws if the download fails or doesn't match.</summary>
    public static async Task<string> GetAsync(FontPreset preset)
    {
        if (Downloaded(preset) is { } existing)
            return existing;

        byte[] data = await Http.Client.GetByteArrayAsync(preset.Url);
        if (!IsExpected(preset, data))
        {
            Logger.Warn(LogIdent, $"{preset.Id}: downloaded file doesn't match its hash");
            throw new InvalidDataException(Loc.T("ModFiles.FontDownloadBad"));
        }

        Directory.CreateDirectory(Folder);
        string temp = PathOf(preset) + ".tmp";
        await File.WriteAllBytesAsync(temp, data);
        File.Move(temp, PathOf(preset), overwrite: true);
        Logger.Info(LogIdent, $"Downloaded {preset.Name} ({data.Length / 1024} KB)");
        return PathOf(preset);
    }

    internal static bool IsExpected(FontPreset preset, byte[] data) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(data)), preset.Sha256, StringComparison.OrdinalIgnoreCase);
}
