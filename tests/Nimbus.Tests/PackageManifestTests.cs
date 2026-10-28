using Nimbus.Roblox;

namespace Nimbus.Tests;

public class PackageManifestTests
{
    private const string Sample =
        "v0\r\n" +
        "RobloxApp.zip\r\n" +
        "ABCDEF0123456789ABCDEF0123456789\r\n" +
        "137795773\r\n" +
        "174128171\r\n" +
        "content-terrain.zip\r\n" +
        "0123456789abcdef0123456789abcdef\r\n" +
        "4807\r\n" +
        "47610\r\n";

    [Fact]
    public void ParsesEveryPackage()
    {
        var packages = PackageManifest.Parse(Sample);

        Assert.Equal(2, packages.Count);
        Assert.Equal(new Package("RobloxApp.zip", "abcdef0123456789abcdef0123456789", 137795773, 174128171), packages[0]);
        Assert.Equal("content-terrain.zip", packages[1].Name);
        Assert.Equal(4807, packages[1].PackedSize);
    }

    [Fact]
    public void WorksWithoutTrailingNewline() =>
        Assert.Equal(2, PackageManifest.Parse(Sample.TrimEnd()).Count);

    [Fact]
    public void RejectsUnknownFormat() =>
        Assert.Throws<InvalidDataException>(() => PackageManifest.Parse("v1\nRobloxApp.zip\nabc\n1\n2\n"));

    [Fact]
    public void RejectsEmptyManifest() =>
        Assert.Throws<InvalidDataException>(() => PackageManifest.Parse("v0\n"));

    [Fact]
    public void EveryMappedPlayerPackageHasAFolder()
    {
        foreach (var (name, folder) in PackageMap.Player)
        {
            Assert.EndsWith(".zip", name);
            Assert.True(folder.Length == 0 || folder.EndsWith('\\'), $"{name} maps to '{folder}'");
        }
    }
}
