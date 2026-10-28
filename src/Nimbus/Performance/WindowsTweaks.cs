using Microsoft.Win32;
using Nimbus.Core;

namespace Nimbus.Performance;

/// <summary>
/// Per-app Windows options for Roblox's EXE, the same ones Windows Settings writes:
/// "Graphics settings → High performance" and "Compatibility → Disable fullscreen optimizations".
/// Each Roblox update has a new EXE path, so the options move with it and old paths are cleaned up.
/// </summary>
internal static class WindowsTweaks
{
    private const string LogIdent = "WindowsTweaks";

    public const string GpuKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string HighPerformanceGpu = "GpuPreference=2;";
    public const string NoFullscreenOptimizations = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    /// <summary>Points the enabled options at <paramref name="exePath"/> and removes every option Nimbus set before.</summary>
    public static void Apply(string exePath, PerformanceSettings settings, AppState state, RegistryKey? root = null)
    {
        root ??= Registry.CurrentUser;

        foreach (string old in state.TweakedExePaths.Where(p => !string.Equals(p, exePath, StringComparison.OrdinalIgnoreCase)))
            Remove(root, old);

        if (settings.HighPerformanceGpu)
            SetValue(root, GpuKey, exePath, HighPerformanceGpu);
        else
            RemoveGpu(root, exePath);

        if (settings.DisableFullscreenOptimizations)
            AddLayer(root, exePath);
        else
            RemoveLayer(root, exePath);

        state.TweakedExePaths = settings.HighPerformanceGpu || settings.DisableFullscreenOptimizations ? [exePath] : [];
    }

    /// <summary>Removes everything Nimbus set (on uninstall).</summary>
    public static void RemoveAll(AppState state, RegistryKey? root = null)
    {
        root ??= Registry.CurrentUser;
        foreach (string path in state.TweakedExePaths)
            Remove(root, path);
        state.TweakedExePaths = [];
    }

    private static void Remove(RegistryKey root, string exePath)
    {
        RemoveGpu(root, exePath);
        RemoveLayer(root, exePath);
    }

    private static void RemoveGpu(RegistryKey root, string exePath)
    {
        using var key = root.OpenSubKey(GpuKey, writable: true);
        // Only remove the value if it's still the one Nimbus wrote.
        if (key?.GetValue(exePath) as string == HighPerformanceGpu)
            key.DeleteValue(exePath, throwOnMissingValue: false);
    }

    /// <summary>Layers values look like "~ FLAG1 FLAG2"; other flags (e.g. DPI settings) are kept.</summary>
    private static void AddLayer(RegistryKey root, string exePath)
    {
        var flags = ReadLayers(root, exePath);
        if (flags.Contains(NoFullscreenOptimizations, StringComparer.OrdinalIgnoreCase))
            return;

        flags.Add(NoFullscreenOptimizations);
        SetValue(root, LayersKey, exePath, "~ " + string.Join(' ', flags));
    }

    private static void RemoveLayer(RegistryKey root, string exePath)
    {
        var flags = ReadLayers(root, exePath);
        if (flags.RemoveAll(f => f.Equals(NoFullscreenOptimizations, StringComparison.OrdinalIgnoreCase)) == 0)
            return;

        using var key = root.OpenSubKey(LayersKey, writable: true);
        if (key is null)
            return;

        if (flags.Count == 0)
            key.DeleteValue(exePath, throwOnMissingValue: false);
        else
            key.SetValue(exePath, "~ " + string.Join(' ', flags));
    }

    private static List<string> ReadLayers(RegistryKey root, string exePath)
    {
        using var key = root.OpenSubKey(LayersKey);
        string value = key?.GetValue(exePath) as string ?? "";
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "~").ToList();
    }

    private static void SetValue(RegistryKey root, string keyPath, string name, string value)
    {
        using var key = root.CreateSubKey(keyPath);
        if (key.GetValue(name) as string == value)
            return;

        key.SetValue(name, value);
        Logger.Info(LogIdent, $"{Path.GetFileName(keyPath)}: {value} for {name}");
    }
}
