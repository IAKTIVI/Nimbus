using System.Text.RegularExpressions;
using Nimbus.Core;

namespace Nimbus.Tests;

public partial class LanguageTests
{
    private static string SourceDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nimbus.slnx")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "Nimbus");
        }
    }

    private static Dictionary<string, string> English => Loc.Load("en")!;

    [Fact]
    public void EveryLanguageIsPackedIntoNimbus()
    {
        foreach (var language in Loc.Languages)
            Assert.NotNull(Loc.Load(language.Code));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt")]
    public void TranslationsHaveEveryTextWithTheSamePlaceholders(string code)
    {
        var translated = Loc.Load(code)!;
        var missing = English.Keys.Except(translated.Keys).ToList();
        var extra = translated.Keys.Except(English.Keys).ToList();
        Assert.True(missing.Count == 0, $"{code} is missing: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"{code} has unknown keys: {string.Join(", ", extra)}");

        foreach (var (key, text) in English)
        {
            var expected = Placeholders(text);
            var actual = Placeholders(translated[key]);
            Assert.True(expected.SetEquals(actual), $"{code} {key}: placeholders {string.Join(",", actual)} instead of {string.Join(",", expected)}");
            Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{code} {key} is empty");
        }
    }

    [Fact]
    public void EveryKeyTheCodeUsesExists()
    {
        var used = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(SourceDir, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            if (file.EndsWith(".xaml", StringComparison.Ordinal))
                used.UnionWith(XamlKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
            else if (file.EndsWith(".cs", StringComparison.Ordinal))
                used.UnionWith(CodeKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
        }

        // Keys built from parts in code
        used.UnionWith(new[] { "Launch", "Engine", "Mods", "Performance", "Integrations", "Widgets", "History", "Profiles", "Appearance", "About" }.Select(p => "Main." + p));
        used.UnionWith(Enumerable.Range(0, 4).Select(i => "Summary.Texture" + i));
        used.UnionWith(Mods.CursorPresets.All.Select(p => "Cursor." + p.Id));
        used.UnionWith(Mods.SoundPresets.All.Select(p => "Sound." + p.Id));
        used.UnionWith(Nimbus.UI.WidgetHost.Choices.Select(k => "Widgets." + k));
        used.UnionWith(Nimbus.UI.WidgetHost.Choices.Where(k => k != WidgetKind.Music).Select(k => "Widgets." + k + "Desc"));

        var missing = used.Where(k => !English.ContainsKey(k) && k != "Main." && k != "Cursor." && k != "Sound." && k != "Summary.Texture" && k != "Appearance.Color" && k != "Widgets.").ToList();
        Assert.True(missing.Count == 0, "Not in en.txt: " + string.Join(", ", missing));
    }

    [Fact]
    public void ParsesTheFileFormat()
    {
        var texts = Loc.Parse("# comment\nA.B = Hello\r\nA.C = Two\\nlines\n\nbroken line\nA.D = x = y");
        Assert.Equal("Hello", texts["A.B"]);
        Assert.Equal("Two\nlines", texts["A.C"]);
        Assert.Equal("x = y", texts["A.D"]);
        Assert.Equal(3, texts.Count);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("PT", "pt")]
    [InlineData("ja", "en")]
    [InlineData(null, "en")]
    public void UnknownLanguagesFallBackToEnglish(string? code, string expected) =>
        Assert.Equal(expected, Loc.Resolve(code));

    [Fact]
    public void MissingKeysShowTheKey() => Assert.Equal("No.Such.Key", Loc.T("No.Such.Key"));

    private static HashSet<string> Placeholders(string text) => [.. PlaceholderRegex().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\{l:T ([A-Za-z0-9.\-]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.T\(""([A-Za-z0-9.\-]+)""")]
    private static partial Regex CodeKey();
}
