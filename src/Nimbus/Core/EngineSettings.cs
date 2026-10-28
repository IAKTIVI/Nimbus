namespace Nimbus.Core;

public enum GraphicsApi
{
    Automatic,
    Direct3D11,
    Vulkan,
    OpenGL,
}

/// <summary>
/// The friendly engine options. Each one maps to FastFlags on Roblox's allowlist (see Roblox.FastFlags).
/// A record so profiles can be compared and copied by value.
/// </summary>
public sealed record EngineSettings
{
    public GraphicsApi GraphicsApi { get; set; }

    /// <summary>1 (off), 2 or 4 samples; null lets Roblox decide.</summary>
    public int? Msaa { get; set; }

    /// <summary>0 (lowest) to 3 (highest); null lets Roblox decide.</summary>
    public int? TextureQuality { get; set; }

    /// <summary>1 (lowest) to 21 (highest); null uses Roblox's in-game graphics slider.</summary>
    public int? QualityLevel { get; set; }

    public bool GraySky { get; set; }

    public bool HideGrass { get; set; }

    public bool StillGrass { get; set; }

    public bool LowDetailMeshes { get; set; }

    public bool FreezeLighting { get; set; }

    /// <summary>A personal display choice: profiles never change it.</summary>
    public bool ExclusiveFullscreen { get; set; }

    /// <summary>Render at full resolution even when Windows display scaling is above 100%.</summary>
    public bool FullResolution { get; set; }
}

public sealed record Profile(string Name, EngineSettings Engine);

internal static class Profiles
{
    public const int MaxNameLength = 32;

    public static readonly Profile Potato = new("Potato", new EngineSettings
    {
        Msaa = 1,
        TextureQuality = 0,
        QualityLevel = 1,
        GraySky = true,
        HideGrass = true,
        StillGrass = true,
        LowDetailMeshes = true,
        FreezeLighting = true,
    });

    public static readonly Profile Balanced = new("Balanced", new EngineSettings
    {
        FullResolution = true,
    });

    public static readonly Profile Quality = new("Quality", new EngineSettings
    {
        Msaa = 4,
        TextureQuality = 3,
        QualityLevel = 21,
        FullResolution = true,
    });

    public static IReadOnlyList<Profile> BuiltIn { get; } = [Potato, Balanced, Quality];

    /// <summary>Copies a profile's options onto the current settings, keeping the user's fullscreen choice.</summary>
    public static void Apply(Settings settings, Profile profile) =>
        settings.Engine = profile.Engine with { ExclusiveFullscreen = settings.Engine.ExclusiveFullscreen };

    public static bool Matches(EngineSettings a, EngineSettings b) =>
        a with { ExclusiveFullscreen = false } == b with { ExclusiveFullscreen = false };

    /// <summary>The profile the current settings match (built-ins first), or null when they've been customised.</summary>
    public static Profile? Current(Settings settings) =>
        BuiltIn.Concat(settings.Profiles).FirstOrDefault(p => Matches(p.Engine, settings.Engine));

    /// <summary>Null when the name is fine, otherwise what's wrong with it.</summary>
    public static string? ValidateName(Settings settings, string name, string? renaming = null)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Loc.T("Profile.NameEmpty");
        if (name.Length > MaxNameLength)
            return Loc.T("Profile.NameLong", MaxNameLength);

        bool taken = BuiltIn.Concat(settings.Profiles).Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p.Name, renaming, StringComparison.OrdinalIgnoreCase));

        return taken ? Loc.T("Profile.NameTaken") : null;
    }

    /// <summary>A short human summary, e.g. "Textures: Lowest - AA: Off - Gray sky".</summary>
    public static string Summarize(EngineSettings e)
    {
        var parts = new List<string>();

        if (e.GraphicsApi != GraphicsApi.Automatic)
            parts.Add(e.GraphicsApi == GraphicsApi.Direct3D11 ? "DirectX 11" : e.GraphicsApi.ToString());
        if (e.Msaa is { } msaa)
            parts.Add(msaa <= 1 ? Loc.T("Summary.AaOff") : Loc.T("Summary.Aa", msaa));
        if (e.TextureQuality is { } textures)
            parts.Add(Loc.T("Summary.Textures", Loc.T("Summary.Texture" + Math.Clamp(textures, 0, 3))));
        if (e.QualityLevel is { } level)
            parts.Add(Loc.T("Summary.Quality", level));
        if (e.GraySky)
            parts.Add(Loc.T("Summary.GraySky"));
        if (e.HideGrass)
            parts.Add(Loc.T("Summary.NoGrass"));
        if (e.StillGrass)
            parts.Add(Loc.T("Summary.StillGrass"));
        if (e.LowDetailMeshes)
            parts.Add(Loc.T("Summary.SimpleMeshes"));
        if (e.FreezeLighting)
            parts.Add(Loc.T("Summary.FrozenLighting"));
        if (e.FullResolution)
            parts.Add(Loc.T("Summary.FullResolution"));

        return parts.Count == 0 ? Loc.T("Profiles.RobloxDefaults") : string.Join(" - ", parts);
    }

    public static readonly string[] TextureNames = ["Lowest", "Low", "Medium", "High"];
}
