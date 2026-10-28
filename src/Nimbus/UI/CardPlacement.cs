using System.Globalization;
using Nimbus.Core;

namespace Nimbus.UI;

/// <summary>
/// Where the music card sits inside the Roblox window. A moved card is remembered as fractions of the free space
/// (0 = left/top edge, 1 = right/bottom edge), so it stays in the same spot when the window size changes.
/// </summary>
internal static class CardPlacement
{
    // Default spot: top right, below Roblox's own top bar (96-DPI pixels).
    private const double MarginRight = 16;
    private const double MarginTop = 64;

    private static string FilePath => Path.Combine(Paths.Base, "MusicCard.txt");

    public static (double X, double Y)? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            string[] parts = File.ReadAllText(FilePath).Split(';');
            return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public static void Save((double X, double Y) position)
    {
        try
        {
            Directory.CreateDirectory(Paths.Base);
            File.WriteAllText(FilePath, string.Create(CultureInfo.InvariantCulture, $"{position.X:0.####};{position.Y:0.####}"));
        }
        catch (IOException ex)
        {
            Logger.Warn("MusicCard", $"Couldn't save the card position: {ex.Message}");
        }
    }

    /// <summary>Back to the top right.</summary>
    public static void Reset()
    {
        try { File.Delete(FilePath); }
        catch (IOException) { }
    }

    /// <summary>The card's top-left in screen pixels for a Roblox game area at (left, top, width, height).</summary>
    public static (int X, int Y) Position((double X, double Y)? saved, int left, int top, int width, int height, int cardWidth, int cardHeight, double scale)
    {
        int freeX = Math.Max(0, width - cardWidth);
        int freeY = Math.Max(0, height - cardHeight);
        if (saved is not { } s)
            return (left + Math.Max(0, freeX - (int)(MarginRight * scale)), top + Math.Min(freeY, (int)(MarginTop * scale)));

        return (left + (int)Math.Round(Math.Clamp(s.X, 0, 1) * freeX), top + (int)Math.Round(Math.Clamp(s.Y, 0, 1) * freeY));
    }

    /// <summary>Turns where the user dropped the card into fractions of the free space.</summary>
    public static (double X, double Y) ToFractions(int x, int y, int left, int top, int width, int height, int cardWidth, int cardHeight)
    {
        int freeX = Math.Max(1, width - cardWidth);
        int freeY = Math.Max(1, height - cardHeight);
        return (Math.Clamp((x - left) / (double)freeX, 0, 1), Math.Clamp((y - top) / (double)freeY, 0, 1));
    }
}
