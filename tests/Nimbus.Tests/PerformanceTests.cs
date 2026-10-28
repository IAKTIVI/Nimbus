using Microsoft.Win32;
using Nimbus.Core;
using Nimbus.Performance;

namespace Nimbus.Tests;

public sealed class FramerateCapTests : IDisposable
{
    private const string Sample =
        "<roblox version=\"4\">\n\t<External>null</External>\n\t<Item class=\"UserGameSettings\" referent=\"RBX1\">\n\t\t<Properties>\n" +
        "\t\t\t<string name=\"DefaultCameraID\"></string>\n\t\t\t<int name=\"FramerateCap\">240</int>\n\t\t\t<bool name=\"Fullscreen\">true</bool>\n" +
        "\t\t</Properties>\n\t</Item>\n</roblox>";

    private readonly string _file = Path.Combine(Directory.CreateTempSubdirectory("NimbusFps").FullName, "GlobalBasicSettings_13.xml");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_file)!, recursive: true);

    [Fact]
    public void ChangesOnlyTheFramerateValue()
    {
        File.WriteAllText(_file, Sample);

        Assert.True(FramerateCap.Write(_file, 144));

        Assert.Equal(Sample.Replace(">240<", ">144<"), File.ReadAllText(_file));
        Assert.Equal(144, FramerateCap.Read(_file));
    }

    [Fact]
    public void SameValueIsLeftAlone()
    {
        File.WriteAllText(_file, Sample);
        var before = File.GetLastWriteTimeUtc(_file);

        Assert.False(FramerateCap.Write(_file, 240));
        Assert.Equal(before, File.GetLastWriteTimeUtc(_file));
    }

    [Fact]
    public void AddsTheSettingWhenRobloxHasNotSavedItYet()
    {
        File.WriteAllText(_file, Sample.Replace("\t\t\t<int name=\"FramerateCap\">240</int>\n", ""));

        Assert.True(FramerateCap.Write(_file, 60));

        Assert.Equal(60, FramerateCap.Read(_file));
        Assert.Contains("<Properties>\n\t\t\t<int name=\"FramerateCap\">60</int>\n\t\t\t<string name=\"DefaultCameraID\">", File.ReadAllText(_file));
    }

    [Fact]
    public void MissingFileIsSkipped()
    {
        Assert.False(FramerateCap.Write(_file, 60));
        Assert.Null(FramerateCap.Read(_file));
        Assert.False(File.Exists(_file));
    }

    [Fact]
    public void NoByteOrderMarkIsAdded()
    {
        File.WriteAllText(_file, Sample);
        FramerateCap.Write(_file, 120);
        Assert.Equal((byte)'<', File.ReadAllBytes(_file)[0]);
    }
}

public sealed class WindowsTweaksTests : IDisposable
{
    private const string ExeA = @"C:\Nimbus\Versions\version-aaaaaaaaaaaaaaaa\RobloxPlayerBeta.exe";
    private const string ExeB = @"C:\Nimbus\Versions\version-bbbbbbbbbbbbbbbb\RobloxPlayerBeta.exe";

    private readonly string _keyPath = $@"Software\NimbusTests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public WindowsTweaksTests() => _root = Registry.CurrentUser.CreateSubKey(_keyPath);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
    }

    private string? Value(string key, string name)
    {
        using var k = _root.OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    [Fact]
    public void SetsBothOptionsAndMovesThemToTheNewVersion()
    {
        var settings = new PerformanceSettings { HighPerformanceGpu = true, DisableFullscreenOptimizations = true };
        var state = new AppState();

        WindowsTweaks.Apply(ExeA, settings, state, _root);
        Assert.Equal("GpuPreference=2;", Value(WindowsTweaks.GpuKey, ExeA));
        Assert.Equal("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", Value(WindowsTweaks.LayersKey, ExeA));

        WindowsTweaks.Apply(ExeB, settings, state, _root);
        Assert.Null(Value(WindowsTweaks.GpuKey, ExeA));
        Assert.Null(Value(WindowsTweaks.LayersKey, ExeA));
        Assert.Equal("GpuPreference=2;", Value(WindowsTweaks.GpuKey, ExeB));
        Assert.Equal([ExeB], state.TweakedExePaths);
    }

    [Fact]
    public void KeepsOtherCompatibilityFlags()
    {
        using (var layers = _root.CreateSubKey(WindowsTweaks.LayersKey))
            layers.SetValue(ExeA, "~ HIGHDPIAWARE");
        var state = new AppState();

        WindowsTweaks.Apply(ExeA, new PerformanceSettings { DisableFullscreenOptimizations = true }, state, _root);
        Assert.Equal("~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE", Value(WindowsTweaks.LayersKey, ExeA));

        WindowsTweaks.Apply(ExeA, new PerformanceSettings(), state, _root);
        Assert.Equal("~ HIGHDPIAWARE", Value(WindowsTweaks.LayersKey, ExeA));
        Assert.Empty(state.TweakedExePaths);
    }

    [Fact]
    public void LeavesAGpuChoiceTheUserMadeThemselves()
    {
        using (var gpu = _root.CreateSubKey(WindowsTweaks.GpuKey))
            gpu.SetValue(ExeA, "GpuPreference=1;");

        WindowsTweaks.Apply(ExeA, new PerformanceSettings(), new AppState { TweakedExePaths = [ExeA] }, _root);

        Assert.Equal("GpuPreference=1;", Value(WindowsTweaks.GpuKey, ExeA));
    }

    [Fact]
    public void UninstallRemovesEverything()
    {
        var state = new AppState();
        WindowsTweaks.Apply(ExeA, new PerformanceSettings { HighPerformanceGpu = true, DisableFullscreenOptimizations = true }, state, _root);

        WindowsTweaks.RemoveAll(state, _root);

        Assert.Null(Value(WindowsTweaks.GpuKey, ExeA));
        Assert.Null(Value(WindowsTweaks.LayersKey, ExeA));
        Assert.Empty(state.TweakedExePaths);
    }
}

[Collection("Paths")]
public sealed class CleanerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("NimbusCleaner").FullName;
    private readonly string _originalBase = Paths.Base;

    public CleanerTests()
    {
        Cleaner.DataFolderOverride = Path.Combine(_root, "Roblox");
        Cleaner.TempFolderOverride = Path.Combine(_root, "Temp", "Roblox");
        Paths.OverrideBase(Path.Combine(_root, "Nimbus"));
    }

    public void Dispose()
    {
        Cleaner.DataFolderOverride = null;
        Cleaner.TempFolderOverride = null;
        Paths.OverrideBase(_originalBase);
        Directory.Delete(_root, recursive: true);
    }

    private string Make(string relative, int bytes, TimeSpan? age = null)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (age is { } a)
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - a);
        return path;
    }

    [Fact]
    public void LaunchCleanOnlyRemovesOldLogs()
    {
        string old = Make(@"Roblox\logs\old.log", 100, TimeSpan.FromDays(5));
        string recent = Make(@"Roblox\logs\recent.log", 100, TimeSpan.FromHours(2));
        string notALog = Make(@"Roblox\logs\keep.json", 100, TimeSpan.FromDays(5));

        var result = Cleaner.Clean(CleanupKind.Logs, Cleaner.OldLogAge);

        Assert.Equal(100, result.FreedBytes);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(notALog));
    }

    [Fact]
    public void FilesInUseAreSkipped()
    {
        Make(@"Temp\Roblox\sounds\a.ogg", 10);
        string locked = Make(@"Temp\Roblox\http\b.bin", 20);

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Cleaner.Clean(CleanupKind.TempFiles);
            Assert.Equal(new CleanupResult(10, 1), result);
        }

        Assert.True(File.Exists(locked));
        Assert.False(Directory.Exists(Path.Combine(_root, @"Temp\Roblox\sounds")));
    }

    [Fact]
    public void AssetCacheIncludesItsDatabase()
    {
        Make(@"Roblox\rbx-storage\ab\cdef", 1000);
        Make(@"Roblox\rbx-storage.db", 300);
        Make(@"Roblox\rbx-storage.db-wal", 50);
        Make(@"Roblox\rbx-storage.id", 5);
        Make(@"Roblox\GlobalBasicSettings_13.xml", 7);

        Assert.Equal(1350, Cleaner.Measure(CleanupKind.AssetCache));
    }

    [Fact]
    public void NimbusDownloadsAreMeasured()
    {
        Make(@"Nimbus\Downloads\0123456789abcdef0123456789abcdef", 4096);
        Assert.Equal(4096, Cleaner.Measure(CleanupKind.NimbusDownloads));
    }
}
