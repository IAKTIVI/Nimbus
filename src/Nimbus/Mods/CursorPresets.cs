using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nimbus.Mods;

/// <summary>A cursor style Nimbus draws itself (no image files shipped).</summary>
public sealed record CursorPreset(string Id, string Name, Action<DrawingContext> Draw);

/// <summary>
/// Simple game cursors drawn on Roblox's 64x64 cursor canvas. Roblox centres the image on the mouse position,
/// so every shape is centred on (32, 32). White with a black outline, so they show up on any background.
/// </summary>
internal static class CursorPresets
{
    private const double C = ModAssets.CursorSize / 2.0;

    private static readonly Brush Fill = Frozen(Brushes.White);
    private static readonly Brush Outline = Frozen(new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)));

    public static IReadOnlyList<CursorPreset> All { get; } =
    [
        new("dot", "Dot", dc => Dot(dc, 3.5)),
        new("circle", "Circle", dc => Ring(dc, 8)),
        new("circle-dot", "Circle + dot", dc =>
        {
            Ring(dc, 9);
            Dot(dc, 2);
        }),
        new("crosshair", "Crosshair", dc => Plus(dc, arm: 9, gap: 0)),
        new("gap-crosshair", "Gap crosshair", dc => Plus(dc, arm: 10, gap: 3)),
    ];

    public static CursorPreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>Renders a preset to a 64x64 bitmap. Needs a WPF (STA) thread.</summary>
    public static BitmapSource Render(CursorPreset preset)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            preset.Draw(dc);

        var bitmap = new RenderTargetBitmap(ModAssets.CursorSize, ModAssets.CursorSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Dot(DrawingContext dc, double radius)
    {
        dc.DrawEllipse(Outline, null, new Point(C, C), radius + 1.5, radius + 1.5);
        dc.DrawEllipse(Fill, null, new Point(C, C), radius, radius);
    }

    private static void Ring(DrawingContext dc, double radius)
    {
        dc.DrawEllipse(null, new Pen(Outline, 4), new Point(C, C), radius, radius);
        dc.DrawEllipse(null, new Pen(Fill, 2), new Point(C, C), radius, radius);
    }

    /// <summary>
    /// A plus sign built from pixel-aligned rectangles, so the 2px lines stay crisp (Minecraft-style when gap is 0).
    /// </summary>
    private static void Plus(DrawingContext dc, double arm, double gap)
    {
        var bars = gap == 0
            ? new[] { new Rect(C - arm, C - 1, arm * 2, 2), new Rect(C - 1, C - arm, 2, arm * 2) }
            : new[]
            {
                new Rect(C - arm, C - 1, arm - gap, 2), new Rect(C + gap, C - 1, arm - gap, 2),
                new Rect(C - 1, C - arm, 2, arm - gap), new Rect(C - 1, C + gap, 2, arm - gap),
            };

        foreach (var bar in bars)
            dc.DrawRectangle(Outline, null, Rect.Inflate(bar, 1, 1));
        foreach (var bar in bars)
            dc.DrawRectangle(Fill, null, bar);
    }

    private static Brush Frozen(Brush brush)
    {
        var clone = brush.Clone();
        clone.Freeze();
        return clone;
    }
}
