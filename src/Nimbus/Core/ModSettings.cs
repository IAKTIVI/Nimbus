namespace Nimbus.Core;

public enum DeathSoundChoice
{
    Default,

    /// <summary>The classic "oof": Roblox still ships it as content\sounds\oof.ogg.</summary>
    Classic,

    /// <summary>One of the sounds Nimbus makes itself (see ModSettings.DeathSoundPreset).</summary>
    Nimbus,

    /// <summary>One of the user's own sound files (Nimbus\Mods\Sounds, see ModSettings.CustomSoundName).</summary>
    Custom,
}

public enum CursorChoice
{
    Default,

    /// <summary>The older black / gray arrows Roblox still ships in content\textures.</summary>
    Classic,

    /// <summary>Nimbus\Mods\Cursor.png: either a built-in preset (see ModSettings.CursorPreset) or the user's own image.</summary>
    Custom,
}

/// <summary>File mods applied to the Roblox install before every launch.</summary>
public sealed class ModSettings
{
    public bool RemoveWaterTexture { get; set; }

    public DeathSoundChoice DeathSound { get; set; }

    /// <summary>With DeathSound = Nimbus: the built-in sound in use (e.g. "blip").</summary>
    public string? DeathSoundPreset { get; set; }

    /// <summary>With DeathSound = Custom: the file name in Nimbus\Mods\Sounds that's in use.</summary>
    public string? CustomSoundName { get; set; }

    public CursorChoice Cursor { get; set; }

    /// <summary>With Cursor = Custom: the built-in preset in use (e.g. "crosshair"), or null for the user's own image.</summary>
    public string? CursorPreset { get; set; }

    /// <summary>File name of the user's own cursor image, for display only.</summary>
    public string? CustomCursorName { get; set; }

    public bool UseCustomFont { get; set; }

    /// <summary>The downloaded free font in use (e.g. "monocraft"), or null for the user's own file.</summary>
    public string? FontPreset { get; set; }

    public string? CustomFontName { get; set; }
}
