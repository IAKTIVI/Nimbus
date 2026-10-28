using System.Globalization;
using System.IO.Compression;

namespace Nimbus.Core;

public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// All UI text. Each language is one "Key = Text" file in src\Nimbus\Lang (packed into the EXE at build time).
/// Missing keys fall back to English. To add a language: copy en.txt, translate it, and add it to <see cref="Languages"/>.
/// </summary>
internal static class Loc
{
    public const string Fallback = "en";

    public static readonly LanguageInfo[] Languages =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        new("pt", "Português"),
    ];

    private static Dictionary<string, string> _english = Load(Fallback) ?? [];
    private static Dictionary<string, string> _current = _english;

    public static string Current { get; private set; } = Fallback;

    /// <summary>The language Nimbus starts in when the user hasn't picked one: Windows' own, if Nimbus has it.</summary>
    public static string SystemDefault => Resolve(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>
    /// The language for someone who hasn't picked one: Windows' own for a new install, but English for people who
    /// had Nimbus before languages existed, so an update doesn't suddenly change everything they know.
    /// </summary>
    public static string FirstLanguage(bool existingUser) => existingUser ? Fallback : SystemDefault;

    /// <summary>A supported language code for <paramref name="code"/>, or English.</summary>
    public static string Resolve(string? code) =>
        Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? Fallback;

    /// <summary>Switches the text Nimbus shows from now on. Windows that are already open keep their text.</summary>
    public static void Use(string? code)
    {
        Current = Resolve(code ?? SystemDefault);
        _current = Current == Fallback ? _english : Load(Current) ?? _english;

        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>The text for a key in the current language.</summary>
    public static string T(string key) =>
        _current.TryGetValue(key, out string? text) || _english.TryGetValue(key, out text) ? text : key;

    /// <summary>The text for a key with {0}, {1}... filled in.</summary>
    public static string T(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>All keys and texts of a language, or null if Nimbus doesn't have it.</summary>
    internal static Dictionary<string, string>? Load(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Nimbus.Lang.{code}.txt.gz");
        if (stream is null)
            return null;

        using var unzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(unzip);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>"Key = Text" lines; # starts a comment, \n in a text is a line break.</summary>
    internal static Dictionary<string, string> Parse(string file)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in file.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int equals = line.IndexOf(" = ", StringComparison.Ordinal);
            if (line.Length == 0 || line[0] == '#' || equals <= 0)
                continue;
            texts[line[..equals].Trim()] = line[(equals + 3)..].Replace("\\n", "\n");
        }
        return texts;
    }
}
