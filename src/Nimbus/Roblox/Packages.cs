using System.Globalization;
using System.Text.Json.Serialization;

namespace Nimbus.Roblox;

/// <summary>Response of clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer.</summary>
public sealed record ClientVersionInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("clientVersionUpload")] string ClientVersionUpload,
    [property: JsonPropertyName("bootstrapperVersion")] string? BootstrapperVersion);

/// <summary>One entry of a version's rbxPkgManifest.txt.</summary>
public sealed record Package(string Name, string Signature, long PackedSize, long Size);

internal static class PackageManifest
{
    /// <summary>
    /// Format: a "v0" header line, then four lines per package: file name, MD5, packed size, unpacked size.
    /// </summary>
    public static IReadOnlyList<Package> Parse(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0] != "v0")
            throw new InvalidDataException($"Unsupported package manifest version '{(lines.Length > 0 ? lines[0] : "")}'");

        var packages = new List<Package>();
        for (int i = 1; i + 3 < lines.Length && lines[i].Length > 0; i += 4)
        {
            packages.Add(new Package(
                Name: lines[i],
                Signature: lines[i + 1].ToLowerInvariant(),
                PackedSize: long.Parse(lines[i + 2], CultureInfo.InvariantCulture),
                Size: long.Parse(lines[i + 3], CultureInfo.InvariantCulture)));
        }

        if (packages.Count == 0)
            throw new InvalidDataException("Package manifest is empty");

        return packages;
    }
}

internal static class PackageMap
{
    /// <summary>Where each WindowsPlayer package is extracted, relative to the version folder.</summary>
    public static readonly IReadOnlyDictionary<string, string> Player = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = @"",
        ["Libraries.zip"] = @"",
        ["redist.zip"] = @"",
        ["WebView2.zip"] = @"",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",

        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-models.zip"] = @"content\models\",

        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",

        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["extracontent-places.zip"] = @"ExtraContent\places\",
    };

    /// <summary>Listed in the manifest but not needed when Nimbus is the bootstrapper.</summary>
    public static readonly IReadOnlySet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RobloxPlayerInstaller.exe",
        "RobloxPlayerLauncher.exe",
        "WebView2RuntimeInstaller.zip",
    };
}
