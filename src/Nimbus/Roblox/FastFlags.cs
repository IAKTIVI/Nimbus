using System.Globalization;
using System.Text.Json;
using Nimbus.Core;

namespace Nimbus.Roblox;

/// <summary>One entry from the advanced editor, and whether Roblox will actually use it.</summary>
public sealed record CustomFlag(string Name, string Value, bool Allowed);

/// <summary>Turns Nimbus's engine settings into Roblox's ClientSettings\ClientAppSettings.json.</summary>
internal static class FastFlags
{
    private const string LogIdent = "FastFlags";

    /// <summary>
    /// Since 29 Sep 2025 the Roblox client only honours these locally set flags and silently ignores the rest
    /// (DevForum: "Allowlist for local client configuration via Fast Flags"). Roblox may change the list.
    /// </summary>
    public static readonly IReadOnlySet<string> Allowlist = new HashSet<string>(StringComparer.Ordinal)
    {
        // Geometry
        "DFIntCSGLevelOfDetailSwitchingDistance",
        "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23",
        "DFIntCSGLevelOfDetailSwitchingDistanceL34",

        // Rendering
        "FFlagHandleAltEnterFullscreenManually",
        "DFFlagTextureQualityOverrideEnabled",
        "DFIntTextureQualityOverride",
        "FIntDebugForceMSAASamples",
        "DFFlagDisableDPIScale",
        "FFlagDebugGraphicsPreferD3D11",
        "FFlagDebugSkyGray",
        "DFFlagDebugPauseVoxelizer",
        "DFIntDebugFRMQualityLevelOverride",
        "FIntFRMMaxGrassDistance",
        "FIntFRMMinGrassDistance",
        "FFlagDebugGraphicsPreferVulkan",
        "FFlagDebugGraphicsPreferOpenGL",

        // User interface
        "FIntGrassMovementReducedMotionFactor",
    };

    /// <summary>The flags for a set of engine options. Roblox stores every value as text.</summary>
    public static SortedDictionary<string, string> FromEngine(EngineSettings e)
    {
        var flags = new SortedDictionary<string, string>(StringComparer.Ordinal);

        switch (e.GraphicsApi)
        {
            case GraphicsApi.Direct3D11: flags["FFlagDebugGraphicsPreferD3D11"] = "True"; break;
            case GraphicsApi.Vulkan: flags["FFlagDebugGraphicsPreferVulkan"] = "True"; break;
            case GraphicsApi.OpenGL: flags["FFlagDebugGraphicsPreferOpenGL"] = "True"; break;
        }

        if (e.Msaa is { } msaa)
            flags["FIntDebugForceMSAASamples"] = Number(msaa);

        if (e.TextureQuality is { } textures)
        {
            flags["DFFlagTextureQualityOverrideEnabled"] = "True";
            flags["DFIntTextureQualityOverride"] = Number(textures);
        }

        if (e.QualityLevel is { } level)
            flags["DFIntDebugFRMQualityLevelOverride"] = Number(level);

        if (e.GraySky)
            flags["FFlagDebugSkyGray"] = "True";

        if (e.HideGrass)
        {
            flags["FIntFRMMinGrassDistance"] = "0";
            flags["FIntFRMMaxGrassDistance"] = "0";
        }

        if (e.StillGrass)
            flags["FIntGrassMovementReducedMotionFactor"] = "0";

        if (e.LowDetailMeshes)
        {
            flags["DFIntCSGLevelOfDetailSwitchingDistance"] = "0";
            flags["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = "0";
            flags["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = "0";
            flags["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = "0";
        }

        if (e.FreezeLighting)
            flags["DFFlagDebugPauseVoxelizer"] = "True";

        // Roblox handles Alt+Enter as borderless by default; letting Windows handle it gives true fullscreen.
        if (e.ExclusiveFullscreen)
            flags["FFlagHandleAltEnterFullscreenManually"] = "False";

        if (e.FullResolution)
            flags["DFFlagDisableDPIScale"] = "True";

        return flags;
    }

    /// <summary>
    /// Parses the advanced editor's JSON object. Throws <see cref="JsonException"/> or <see cref="FormatException"/>
    /// with a readable message when it isn't valid.
    /// </summary>
    public static IReadOnlyList<CustomFlag> ParseCustom(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        using var doc = JsonDocument.Parse(json, options);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException(Loc.T("Flags.Braces"));

        var flags = new List<CustomFlag>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            string value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => throw new FormatException(Loc.T("Flags.BadValue", property.Name)),
            };

            flags.Add(new CustomFlag(property.Name, value, Allowlist.Contains(property.Name)));
        }

        return flags;
    }

    /// <summary>Toggles first, then the advanced editor's flags on top. Invalid editor JSON is skipped (and logged).</summary>
    public static SortedDictionary<string, string> Build(Settings settings)
    {
        var flags = FromEngine(settings.Engine);

        try
        {
            foreach (var flag in ParseCustom(settings.CustomFlags))
                flags[flag.Name] = flag.Value;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            Logger.Warn(LogIdent, $"Advanced flags skipped, the JSON isn't valid: {ex.Message}");
        }

        return flags;
    }

    /// <summary>Writes the flags into a Roblox version folder. Runs before every launch, so updates never lose them.</summary>
    public static void Apply(string versionDirectory, Settings settings)
    {
        var flags = Build(settings);
        string dir = Path.Combine(versionDirectory, "ClientSettings");
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "ClientAppSettings.json");
        string temp = path + ".tmp";
        using (var stream = File.Create(temp))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in flags)
                writer.WriteString(name, value);
            writer.WriteEndObject();
        }

        File.Move(temp, path, overwrite: true);

        int ignored = flags.Keys.Count(k => !Allowlist.Contains(k));
        Logger.Info(LogIdent, $"Wrote {flags.Count} FastFlags" + (ignored > 0 ? $" ({ignored} not on Roblox's allowlist)" : ""));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
