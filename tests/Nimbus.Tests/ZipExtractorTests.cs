using System.IO.Compression;
using Nimbus.Roblox;

namespace Nimbus.Tests;

public sealed class ZipExtractorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("NimbusZipTest").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeZip(params string[] entryNames)
    {
        string path = Path.Combine(_dir, $"{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in entryNames)
        {
            var entry = zip.CreateEntry(name);
            if (name.EndsWith('\\') || name.EndsWith('/'))
                continue;

            using var writer = new StreamWriter(entry.Open());
            writer.Write($"content of {name}");
        }

        return path;
    }

    [Fact]
    public void ExtractsRobloxStyleBackslashEntries()
    {
        // Real Roblox packages start with a "\" root entry and use backslash folders.
        string zip = MakeZip(@"\", @"materials.json", @"sub\", @"sub\nested.json");
        string target = Path.Combine(_dir, "out", @"PlatformContent\pc\terrain\");

        ZipExtractor.Extract(zip, target);

        Assert.True(File.Exists(Path.Combine(target, "materials.json")));
        Assert.Equal("content of sub\\nested.json", File.ReadAllText(Path.Combine(target, "sub", "nested.json")));
    }

    [Theory]
    [InlineData(@"..\evil.txt")]
    [InlineData(@"sub\..\..\evil.txt")]
    [InlineData("../evil.txt")]
    public void BlocksPathsThatEscapeTheTarget(string entry)
    {
        string zip = MakeZip(entry);
        string target = Path.Combine(_dir, "out");

        Assert.Throws<InvalidDataException>(() => ZipExtractor.Extract(zip, target));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
    }
}
