namespace Nimbus.Mods;

/// <summary>A death sound Nimbus makes itself (no recordings, so nothing anyone else owns).</summary>
public sealed record SoundPreset(string Id, Func<double, double> Wave, double Seconds);

/// <summary>
/// The built-in death sounds, synthesised into a small WAV when they're used. Roblox reads the sound by its
/// content, so a WAV works in place of ouch.ogg. Names come from Lang ("Sound.{Id}").
/// </summary>
internal static class SoundPresets
{
    private const int SampleRate = 22050;

    public static IReadOnlyList<SoundPreset> All { get; } =
    [
        new("blip", Blip, 0.4),
        new("gameover", GameOver, 1.0),
        new("zap", Zap, 0.3),
        new("ding", Ding, 1.0),
        new("thud", Thud, 0.35),
    ];

    public static SoundPreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>The sound as a 16-bit mono WAV file.</summary>
    public static byte[] Render(SoundPreset preset)
    {
        int samples = (int)(preset.Seconds * SampleRate);
        using var stream = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double fade = Math.Min(1, (preset.Seconds - t) / 0.01); // no click at the end
            writer.Write((short)(Math.Clamp(preset.Wave(t) * fade, -1, 1) * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }

    // ---------- The sounds (t in seconds, result -1..1) ----------

    private static double Square(double phase) => phase % 1 < 0.5 ? 1 : -1;

    /// <summary>A falling 8-bit blip, like losing a life.</summary>
    private static double Blip(double t)
    {
        // Phase of a tone sliding exponentially from 880 Hz down to 110 Hz over 0.4 s.
        const double from = 880, to = 110, length = 0.4;
        double k = Math.Log(to / from) / length;
        double phase = from * (Math.Exp(k * t) - 1) / k;
        return 0.3 * Square(phase) * (1 - t / length);
    }

    /// <summary>Three falling notes and a long low one.</summary>
    private static double GameOver(double t)
    {
        (double start, double length, double hz)[] notes = [(0, 0.13, 392), (0.15, 0.13, 330), (0.3, 0.13, 262), (0.45, 0.55, 131)];
        foreach (var (start, length, hz) in notes)
        {
            if (t >= start && t < start + length)
            {
                double local = t - start;
                return 0.28 * Square(hz * local) * Math.Min(1, (length - local) / 0.03);
            }
        }
        return 0;
    }

    /// <summary>A quick laser "pew" sweep.</summary>
    private static double Zap(double t)
    {
        const double from = 1600, to = 180, length = 0.3;
        double k = Math.Log(to / from) / length;
        double phase = from * (Math.Exp(k * t) - 1) / k;
        return 0.25 * Square(phase) * Math.Exp(-t * 6);
    }

    /// <summary>A soft bell.</summary>
    private static double Ding(double t)
    {
        double attack = Math.Min(1, t / 0.005);
        return attack * Math.Exp(-t * 4.5) * (0.5 * Math.Sin(2 * Math.PI * 1046.5 * t) + 0.15 * Math.Sin(2 * Math.PI * 2093 * t));
    }

    /// <summary>A low, dull thump.</summary>
    private static double Thud(double t)
    {
        const double from = 140, to = 45, length = 0.35;
        double k = Math.Log(to / from) / length;
        double phase = from * (Math.Exp(k * t) - 1) / k;
        return 0.8 * Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 11) * Math.Min(1, t / 0.003);
    }
}
